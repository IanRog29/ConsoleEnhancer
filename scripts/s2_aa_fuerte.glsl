// ---------------------------------------------------------------------------------------------------------------------------------
// El suavizado de bordes de este fichero (la parte FXAA: luma, deteccion del borde, recorrido a los dos lados y mezcla) sigue
// NVIDIA FXAA 3.11 de Timothy Lottes (Fxaa3_11.h), reescrito para los shaders de usuario de mpv. Origen y licencia:
// https://github.com/NVIDIAGameWorks/GraphicsSamples/blob/master/samples/es3-kepler/FXAA/FXAA3_11.h (BSD-3-Clause):
//
// Copyright (c) 2014-2015, NVIDIA CORPORATION. All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following
// conditions are met:
//  * Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
//  * Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer
//    in the documentation and/or other materials provided with the distribution.
//  * Neither the name of NVIDIA CORPORATION nor the names of its contributors may be used to endorse or promote products derived
//    from this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
// THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE COPYRIGHT
// OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
// NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING
// IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// El resto (quitar el tramado de 5 bits, umbrales y pasadas propias) es de ConsoleEnhancer, GPL-3.0 (LICENSE, LICENSE-EXCEPTION.txt).
// ---------------------------------------------------------------------------------------------------------------------------------
// Suavizado de bordes (FXAA 3.11, calidad alta) de la imagen de la capturadora, antes de que la vean la
// generacion de fotogramas, DLSS y DLSS 5: sus dientes de sierra no llegan a ninguno de ellos, tampoco a las
// imagenes generadas. A 1440p y solo en imagenes reales cuesta mucho menos que suavizar a 4K en cada imagen mostrada
//
// 11-11: el tramado se quita SOLO donde lo hay (puerta de tramado, ver S2Paso). Las capturas crudas de Ocarina del 10-11 no tienen
// tramado de 5 bits (es un remake con luz moderna: niveles de 8 bits seguidos y ruido de +-1 nivel) y este filtro les alisaba la
// textura suave: en el render de mpv de esas capturas, en lo de poco contraste quedaba el 41-64 % del grano de lo oscuro y el 57-80 %
// del de los medios (con la puerta, 92-98 %: lo que falta es el FXAA de los bordes). En la salida a 4K de la app (fuente = esas
// fotos en panoramica lenta, modo 60 con generacion, DLSS 5 apagado, K) el error de baja frecuencia frente a la imagen ideal bajaba
// en lo oscuro 0,77 -> 0,55 y en la niebla 0,56 -> 0,45-0,49, y el temblor (cambio del error de una imagen a otra) 0,89 ->
// 0,58-0,64 y 0,50 -> 0,34-0,38: la neblina recupera montes, arboles y postes del fondo y las paredes su grano. Con tramado de 5 bits de
// verdad (comp.png: azar, Bayer, magico, rango limitado) el resultado es el mismo que antes (diferencia media <= 0,01/255)
//
// 10-11: quitar el tramado va ahora en una pasada de computo (guia, horizontal, vertical y luego FXAA) y mucho mas a fondo en
// lo plano de una imagen de 5 bits. Por que: en la partida real del 09-11 (Ocarina) el estimador de movimiento del escalado
// daba, al moverse, vectores de 9-18 px de largo medio con un vector medio de 1,5-5 (sin orden) y DLSS se apoyaba en la imagen
// nueva estirada en el 29-53 % de la pantalla (cuartiles de 177 ventanas de 10 s); parado, el coste de emparejar bajaba a
// 0,0004-0,0008: la imagen no cambia de una a otra, asi que el tramado es un patron fijo (como el de 4x4 del N64), no ruido
// nuevo en cada imagen. El estimador busca a media resolucion (1280x720, bloques de 8x8): alli el patron fijo queda como un
// dibujo de 2 px que agarra a la pantalla los bloques de lo oscuro y la niebla (modelo del estimador: con el filtro del 09-11
// 0-62 % de vectores equivocados ahi, 1-84 % si la consola escala x2; con este 0-2 % y 0-21 %). El filtro del 09-11 solo
// actuaba donde el 3x3 no pasaba de ~1,5 pasos: fallaba donde el tramado da 3 niveles (degradados), a 2 px de los bordes y
// con el grano de 2 px de una imagen escalada por la consola
// Medido sin conexion (modelo en C# igual al shader, +-1/255; error frente a la imagen sin tramar, 1/255): patron fijo de 4x4
// en zonas suaves 0,37-0,53 -> 0,22-0,40, bordes 1,46 -> 0,68, casi negro 0,40 -> 0,13; escalado x2 por la consola 0,53-0,73 ->
// 0,32-0,52; ruido nuevo en cada imagen 0,76-0,80 -> 0,58-0,63; foto de un juego actual sin tramado 1,87 -> 1,70 (toca menos)
// En la app (10-11, fuentes sinteticas a 1440p con tramado de 5 bits, parpadeo de la salida a 4K con FlickF, antes -> ahora):
// modo 60 con generacion y DLSS 5 apagado, banco sin las capturas de la app: cueva plana con patron fijo de 4x4 al moverse,
// cambio de imagen a imagen (chg2) en lo oscuro 0,057 -> 0,039 y en la niebla 0,035 -> 0,016, alternancia real/generada (jit2)
// en la niebla 0,046 -> 0,015, grano 0,38 -> 0,16 y 0,34 -> 0,06, la parte con detalle tambien mas quieta (0,51 -> 0,40). Pero
// en la fuente N64 con textura suave la niebla mejora (jit2 0,071 -> 0,045) y lo oscuro con textura y la parte con detalle
// tiemblan mas (0,164 -> 0,192 y 0,52 -> 0,73; 3 pasadas, tambien con la puerta estricta). Sin generacion todo igual o mejor
// (niebla 0,072 -> 0,041); modo 30: oscuro 0,146 -> 0,112 y niebla 0,108 -> 0,074. Con DLSS 5 encendido la niebla mejora
// (0,101 -> 0,063) pero la parte con detalle tiembla el doble (0,50 -> 1,08-1,24 en 3 pasadas, con y sin puerta): DLSS 5
// reacciona al quitar el tramado de lo plano. 60 imagenes reales en todas (p50 16,7 ms); grafica +4-6 puntos con DLSS 5
// apagado (71 -> 75-77 %). Por eso la app lo pone SOLO con DLSS 5 apagado (Config.StrongDither, ini tramado_fuerte) y L3 del
// mando lo cambia en caliente por el de antes (s2_aa.glsl) para compararlos en el juego de verdad: falta verlo en la consola

//!PARAM s2_tramado
//!DESC 1 = quitar el tramado en las zonas casi planas (niebla, cielos, sombras); 0 = no (ini quitar_tramado)
//!TYPE int
//!MINIMUM 0
//!MAXIMUM 1
1

//!HOOK MAIN
//!BIND HOOKED
//!WHEN HOOKED.w 2600 <
//!COMPUTE 32 8
//!DESC S2 AA

#define EDGE_THRESHOLD 0.125 // 08-11: punto medio (0.09/0.02/0.5 emborronaba la hierba; 0.166/0.0625/0.25 daba +10 % de parpadeo con la generacion)
#define EDGE_THRESHOLD_MIN 0.047 // 09-11: 0.0312 encendia el FXAA sobre el tramado oscuro
#define SUBPIX 0.25 // 09-11: 0.375 emborronaba detalle de 1 pixel que DLSS ya no recupera

// Cada grupo (32x8 puntos) carga su trozo con 5 de margen (42x18) en memoria compartida y hace alli las tres etapas: la imagen
// se lee y se escribe una vez, como el FXAA solo
shared vec3 sC[18][42];
shared float sG[18][42];
shared float sD[18][42];
shared uint sAny;
shared vec3 sH[16][32];
shared vec2 sP[16][32];

// 11-11: PUERTA DE TRAMADO. Las capturas reales de Ocarina (10-11, juegos\debug\tramado) NO tienen tramado de 5 bits: niveles de
// 8 bits seguidos (0,1,2,3...; en un nivel de 5 bits el 34-37 % de los puntos, como al azar), ruido de +-1 nivel. Aun asi el filtro
// tomaba por "5 bits" el 24-56 % de los 3x3 con algo de contraste (diferencias de 0-1 niveles pasan la prueba de "multiplo de 8,25")
// y con la otra via (3x3 de menos de 9-13 niveles) alisaba casi toda la textura suave: la neblina perdia los montes y arboles del
// fondo y las paredes oscuras su grano (medido en la salida a 4K de la app: ver informe del agente niebla). Ahora solo se quita el
// tramado donde lo hay: un 3x3 cuyas 12 diferencias entre vecinos (por canal) son todas 0-1 niveles o pasos limpios de 5 bits
// (8,25 o 16,5 +-2) y con al menos 2 pasos, extendido 2 puntos alrededor. comp.png (9 paneles x 8 tramados sinteticos): marca el
// 95-100 % de las celdas con tramado de 5 bits (azar, Bayer, magico, rango limitado) y 0 % de las de 8 bits (16 % en la de bordes
// limpios); capturas reales de Ocarina: 0,01-0,16 %. Lo que no tiene tramado sale como del FXAA solo
float S2Paso(float d) { // 1 = paso de 5 bits, 0 = igual, -1 = otra cosa
    float ad = abs(d) * 255.0;
    if (ad < 2.0) return 0.0;
    float e = abs(ad - 8.25 * floor(ad / 8.25 + 0.5));
    return (e < 2.0 && ad < 20.0) ? 1.0 : -1.0;
}

vec2 gpos;
float S2L(vec2 p) { return dot(HOOKED_tex(p).rgb, vec3(0.299, 0.587, 0.114)); }
float S2O(float x, float y) { return dot(HOOKED_tex(gpos + vec2(x, y) * HOOKED_pt).rgb, vec3(0.299, 0.587, 0.114)); }

vec4 S2Fxaa(vec4 color, vec3 dd, float S) {
    vec2 pos = gpos;
    vec2 pt = HOOKED_pt;
    float lumaM = dot(color.rgb, vec3(0.299, 0.587, 0.114));
    float lumaS = S2O(0.0, 1.0), lumaE = S2O(1.0, 0.0), lumaN = S2O(0.0, -1.0), lumaW = S2O(-1.0, 0.0);
    float rangeMax = max(max(max(lumaS, lumaE), max(lumaN, lumaW)), lumaM);
    float rangeMin = min(min(min(lumaS, lumaE), min(lumaN, lumaW)), lumaM);
    float range = rangeMax - rangeMin;
    // donde se quita el tramado fuerte (S) el FXAA solo para bordes de 3 pasos o mas: con 2 se encendia en el tramado de 3
    // niveles de un degradado y devolvia el punto tramado en vez del limpio
    if (range < max(mix(EDGE_THRESHOLD_MIN, 0.094, S), rangeMax * EDGE_THRESHOLD)) return vec4(dd, color.a);

    float lumaNW = S2O(-1.0, -1.0), lumaSE = S2O(1.0, 1.0), lumaNE = S2O(1.0, -1.0), lumaSW = S2O(-1.0, 1.0);
    float lumaNS = lumaN + lumaS, lumaWE = lumaW + lumaE;
    float subpixNSWE = lumaNS + lumaWE;
    float edgeHorz1 = -2.0 * lumaM + lumaNS;
    float edgeVert1 = -2.0 * lumaM + lumaWE;
    float lumaNESE = lumaNE + lumaSE;
    float lumaNWNE = lumaNW + lumaNE;
    float edgeHorz2 = -2.0 * lumaE + lumaNESE;
    float edgeVert2 = -2.0 * lumaN + lumaNWNE;
    float lumaNWSW = lumaNW + lumaSW;
    float lumaSWSE = lumaSW + lumaSE;
    float edgeHorz3 = -2.0 * lumaW + lumaNWSW;
    float edgeVert3 = -2.0 * lumaS + lumaSWSE;
    float edgeHorz = abs(edgeHorz3) + abs(edgeHorz1) * 2.0 + abs(edgeHorz2);
    float edgeVert = abs(edgeVert3) + abs(edgeVert1) * 2.0 + abs(edgeVert2);
    float subpixNWSWNESE = lumaNWSW + lumaNESE;
    float lengthSign = pt.x;
    bool horzSpan = edgeHorz >= edgeVert;
    float subpixA = subpixNSWE * 2.0 + subpixNWSWNESE;
    if (!horzSpan) { lumaN = lumaW; lumaS = lumaE; } else { lengthSign = pt.y; }
    float subpixB = subpixA / 12.0 - lumaM;
    float gradientN = lumaN - lumaM, gradientS = lumaS - lumaM;
    float lumaNN = lumaN + lumaM, lumaSS = lumaS + lumaM;
    bool pairN = abs(gradientN) >= abs(gradientS);
    float gradient = max(abs(gradientN), abs(gradientS));
    if (pairN) lengthSign = -lengthSign;
    float subpixC = clamp(abs(subpixB) / range, 0.0, 1.0);

    vec2 posB = pos;
    vec2 offNP = horzSpan ? vec2(pt.x, 0.0) : vec2(0.0, pt.y);
    if (!horzSpan) posB.x += lengthSign * 0.5; else posB.y += lengthSign * 0.5;
    vec2 posN = posB - offNP;
    vec2 posP = posB + offNP;
    float subpixD = -2.0 * subpixC + 3.0;
    float subpixE = subpixC * subpixC;
    if (!pairN) lumaNN = lumaSS;
    float gradientScaled = gradient * 0.25;
    float lumaMM = lumaM - lumaNN * 0.5;
    float subpixF = subpixD * subpixE;
    bool lumaMLTZero = lumaMM < 0.0;
    float lumaEndN = S2L(posN) - lumaNN * 0.5;
    float lumaEndP = S2L(posP) - lumaNN * 0.5;
    bool doneN = abs(lumaEndN) >= gradientScaled;
    bool doneP = abs(lumaEndP) >= gradientScaled;

    // Se sigue el borde a los dos lados, hasta 25 pixeles: las escaleras largas de lineas casi rectas
    const int STEPS = 8;
    float stepLen[8] = float[8](1.0, 1.5, 2.0, 2.0, 2.0, 2.0, 4.0, 8.0);
    for (int i = 0; i < STEPS && !(doneN && doneP); i++) {
        if (!doneN) {
            posN -= offNP * stepLen[i];
            lumaEndN = S2L(posN) - lumaNN * 0.5;
            doneN = abs(lumaEndN) >= gradientScaled;
        }
        if (!doneP) {
            posP += offNP * stepLen[i];
            lumaEndP = S2L(posP) - lumaNN * 0.5;
            doneP = abs(lumaEndP) >= gradientScaled;
        }
    }

    float dstN = horzSpan ? pos.x - posN.x : pos.y - posN.y;
    float dstP = horzSpan ? posP.x - pos.x : posP.y - pos.y;
    bool directionN = dstN < dstP;
    float dst = min(dstN, dstP);
    bool goodSpan = directionN ? ((lumaEndN < 0.0) != lumaMLTZero) : ((lumaEndP < 0.0) != lumaMLTZero);
    float pixelOffset = 0.5 - dst / (dstP + dstN);
    float pixelOffsetGood = goodSpan ? pixelOffset : 0.0;
    float subpixH = subpixF * subpixF * SUBPIX;
    float off = max(pixelOffsetGood, subpixH);
    vec2 posM = pos;
    if (!horzSpan) posM.x += off * lengthSign; else posM.y += off * lengthSign;
    return vec4(HOOKED_tex(posM).rgb, color.a);
}

void hook() {
    ivec2 lid = ivec2(gl_LocalInvocationID.xy);
    ivec2 org = ivec2(gl_WorkGroupID.xy) * ivec2(32, 8) - ivec2(5, 5);
    int li = lid.y * 32 + lid.x;
    if (li == 0) sAny = 0u;
    for (int k = li; k < 18 * 42; k += 256) {
        int ty = k / 42, tx = k - ty * 42;
        sC[ty][tx] = HOOKED_tex((vec2(org + ivec2(tx, ty)) + 0.5) * HOOKED_pt).rgb;
    }
    barrier();
    // 11-11: puerta de tramado de cada 3x3 (ver S2Paso): sin diferencias "de otra cosa" y con 2 pasos o mas. Primero el verde (12
    // parejas) y solo si pasa, rojo y azul: en un juego de 8 bits casi nunca pasa el verde. Si ningun punto del trozo tiene tramado
    // (sAny = 0), el grupo entero se salta la guia y las dos pasadas del filtro y sale el FXAA solo
    for (int k = li; k < 16 * 40; k += 256) {
        int gy = k / 40 + 1, gx = k - (gy - 1) * 40 + 1;
        float steps = 0.0, off = 0.0;
        for (int j = -1; j <= 1; j++) for (int i = -1; i <= 0; i++) {
            float a = S2Paso(sC[gy + j][gx + i + 1].g - sC[gy + j][gx + i].g), b = S2Paso(sC[gy + i + 1][gx + j].g - sC[gy + i][gx + j].g);
            steps += max(a, 0.0) + max(b, 0.0); off += max(-a, 0.0) + max(-b, 0.0);
        }
        if (off < 0.5) {
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 0; i++) {
                vec3 dh = sC[gy + j][gx + i + 1] - sC[gy + j][gx + i];
                vec3 dv = sC[gy + i + 1][gx + j] - sC[gy + i][gx + j];
                float a = S2Paso(dh.r), b = S2Paso(dv.r), a2 = S2Paso(dh.b), b2 = S2Paso(dv.b);
                steps += max(a, 0.0) + max(b, 0.0) + max(a2, 0.0) + max(b2, 0.0);
                off += max(-a, 0.0) + max(-b, 0.0) + max(-a2, 0.0) + max(-b2, 0.0);
            }
        }
        float d = (off < 0.5 && steps > 1.5) ? 1.0 : 0.0;
        sD[gy][gx] = d;
        if (d > 0.5) atomicOr(sAny, 1u);
    }
    barrier();
    bool hayTramado = s2_tramado > 0 && sAny != 0u;
    // Guia: el brillo suavizado con los 8 vecinos (1 2 1 x 1 2 1). El tramado casi desaparece de ella y los bordes siguen: los
    // pesos de abajo comparan guias, no puntos sueltos (un borde de 2 pasos o mas no se cruza aunque el tramado se toque)
    for (int k = li; k < 16 * 40; k += 256) {
        if (!hayTramado) break;
        int gy = k / 40 + 1, gx = k - (gy - 1) * 40 + 1;
        vec3 s = sC[gy - 1][gx - 1] + 2.0 * sC[gy - 1][gx] + sC[gy - 1][gx + 1]
               + 2.0 * sC[gy][gx - 1] + 4.0 * sC[gy][gx] + 2.0 * sC[gy][gx + 1]
               + sC[gy + 1][gx - 1] + 2.0 * sC[gy + 1][gx] + sC[gy + 1][gx + 1];
        sG[gy][gx] = dot(s * 0.0625, vec3(0.299, 0.587, 0.114));
    }
    barrier();
    // Horizontal (9 puntos, gaussiana de 2,5) en las 16 filas que necesita la vertical. S: el 3x3 es "de 5 bits", toda diferencia
    // con el centro es un numero entero de pasos de 8,25/255 (+-2/255: aguanta el rango limitado de la HDMI); un juego actual
    // casi nunca lo cumple en los 24 valores, el N64 siempre. Puerta de textura: S solo cuenta donde la guia no se separa mas de
    // ~1 paso (4-8/255) en los 9 puntos (niebla, oscuro liso, degradados); con textura o detalle va como el 09-11 (solo donde el
    // 3x3 no pasa de ~1,5 pasos, dw). Sin la puerta (medido en la app) la parte con detalle de la imagen temblaba mas con DLSS 5
    // (cambio 0,64 -> 1,15) y el modelo emborronaba la foto con tramado (3,60 frente a 3,33 del 09-11; con la puerta 2,94)
    // S fuerte: vecinos hasta 2-3 pasos del centro (tambien donde el tramado da 3 niveles); si no, hasta ~1,5 pasos
    for (int k = li; k < 16 * 32; k += 256) {
        if (!hayTramado) break;
        int hy = k / 32, hx = k - hy * 32;
        int ty = hy + 1, tx = hx + 5;
        vec3 c = sC[ty][tx], lo = c, hi = c;
        // 11-11: solo dentro de la zona con tramado (la puerta del 3x3 extendida 2 puntos; filas fuera del trozo: la mas cercana).
        // Fuera de ella (todo un juego de 8 bits) no se hace nada mas: mas barato que antes
        float zone = 0.0;
        for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++) zone = max(zone, sD[clamp(ty + j, 1, 16)][tx + i]);
        if (zone < 0.5) { sH[hy][hx] = c; sP[hy][hx] = vec2(0.0); continue; }
        float fail = 0.0;
        for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
            vec3 t = sC[ty + j][tx + i];
            lo = min(lo, t); hi = max(hi, t);
            vec3 d = (t - c) * 255.0;
            vec3 e = abs(d - 8.25 * floor(d / 8.25 + 0.5));
            fail += dot(step(vec3(2.0), e), vec3(1.0));
        }
        vec3 rg = hi - lo;
        float S = 1.0 - smoothstep(0.5, 2.5, fail);
        float dw = 1.0 - smoothstep(9.0 / 255.0, 13.0 / 255.0, max(rg.r, max(rg.g, rg.b)));
        vec3 h = c;
        float gc = sG[ty][tx], tmx = 0.0;
        for (int i = -4; i <= 4; i++) tmx = max(tmx, abs(sG[ty][tx + i] - gc));
        float Sh = S * (1.0 - smoothstep(4.0 / 255.0, 8.0 / 255.0, tmx));
        if (max(Sh, dw) > 0.0) {
            float r0 = mix(10.0, 16.0, Sh) / 255.0, r1 = mix(14.0, 24.0, Sh) / 255.0;
            vec3 acc = vec3(0.0); float aw = 0.0;
            for (int i = -4; i <= 4; i++) {
                vec3 t = sC[ty][tx + i];
                vec3 dd = abs(t - c);
                float w = exp(-float(i * i) * 0.08)
                        * (1.0 - smoothstep(4.0 / 255.0, 8.0 / 255.0, abs(sG[ty][tx + i] - gc)))
                        * (1.0 - smoothstep(r0, r1, max(dd.r, max(dd.g, dd.b))));
                acc += t * w; aw += w;
            }
            h = acc / aw;
        }
        sH[hy][hx] = h; sP[hy][hx] = vec2(S, dw);
    }
    barrier();
    // Vertical (lo mismo sobre lo que dejo la horizontal, con su propia puerta) mezclada con el original por dw, y el FXAA
    ivec2 gid = ivec2(gl_GlobalInvocationID.xy);
    if (gid.x >= int(HOOKED_size.x) || gid.y >= int(HOOKED_size.y)) return;
    gpos = (vec2(gid) + 0.5) * HOOKED_pt;
    int hy0 = lid.y + 4, hx0 = lid.x;
    vec4 color = vec4(sC[lid.y + 5][lid.x + 5], 1.0);
    if (!hayTramado) { imageStore(out_image, gid, S2Fxaa(color, color.rgb, 0.0)); return; }
    vec2 sd = sP[hy0][hx0];
    float gc = sG[lid.y + 5][lid.x + 5], tmx = 0.0;
    if (sd.x > 0.0) for (int j = -4; j <= 4; j++) tmx = max(tmx, abs(sG[lid.y + 5 + j][lid.x + 5] - gc));
    float S = s2_tramado > 0 ? sd.x * (1.0 - smoothstep(4.0 / 255.0, 8.0 / 255.0, tmx)) : 0.0;
    float dw = max(sd.y, S);
    vec3 dd = color.rgb;
    if (s2_tramado > 0 && dw > 0.0) {
        float r0 = mix(10.0, 16.0, S) / 255.0, r1 = mix(14.0, 24.0, S) / 255.0;
        vec3 c = sH[hy0][hx0], acc = vec3(0.0); float aw = 0.0;
        for (int j = -4; j <= 4; j++) {
            vec3 t = sH[hy0 + j][hx0];
            vec3 d3 = abs(t - c);
            float w = exp(-float(j * j) * 0.08)
                    * (1.0 - smoothstep(4.0 / 255.0, 8.0 / 255.0, abs(sG[lid.y + 5 + j][lid.x + 5] - gc)))
                    * (1.0 - smoothstep(r0, r1, max(d3.r, max(d3.g, d3.b))));
            acc += t * w; aw += w;
        }
        dd = mix(color.rgb, acc / aw, dw);
    }
    imageStore(out_image, gid, S2Fxaa(color, dd, S));
}

//!HOOK MAIN
//!BIND HOOKED
//!WHEN HOOKED.w 2600 >
//!DESC S2 AA 4K

// Captura 4K (juegos de mas de 1440p): la pasada del 09-11 tal cual (aqui la de arriba costaria ~1,1 ms mas)

#define EDGE_THRESHOLD 0.125 // 08-11: punto medio (0.09/0.02/0.5 emborronaba la hierba; 0.166/0.0625/0.25 daba +10 % de parpadeo con la generacion)
#define EDGE_THRESHOLD_MIN 0.047 // 09-11: 0.0312 encendia el FXAA sobre el tramado oscuro (ver abajo)
#define SUBPIX 0.25 // 09-11: 0.375 emborronaba detalle de 1 pixel que DLSS ya no recupera

float S2L4(vec2 p) { return dot(HOOKED_tex(p).rgb, vec3(0.299, 0.587, 0.114)); }
float S2Paso4(float d) { // como S2Paso de la pasada de 1440p: 1 = paso de 5 bits, 0 = igual, -1 = otra cosa
    float ad = abs(d) * 255.0;
    if (ad < 2.0) return 0.0;
    float e = abs(ad - 8.25 * floor(ad / 8.25 + 0.5));
    return (e < 2.0 && ad < 20.0) ? 1.0 : -1.0;
}
float S2O4(float x, float y) { return dot(HOOKED_texOff(vec2(x, y)).rgb, vec3(0.299, 0.587, 0.114)); }

vec4 hook() {
    vec4 color = HOOKED_texOff(vec2(0.0, 0.0));
    vec2 pos = HOOKED_pos;
    vec2 pt = HOOKED_pt;
    float lumaM = dot(color.rgb, vec3(0.299, 0.587, 0.114));
    float lumaS = S2O4(0.0, 1.0), lumaE = S2O4(1.0, 0.0), lumaN = S2O4(0.0, -1.0), lumaW = S2O4(-1.0, 0.0);
    float rangeMax = max(max(max(lumaS, lumaE), max(lumaN, lumaW)), lumaM);
    float rangeMin = min(min(min(lumaS, lumaE), min(lumaN, lumaW)), lumaM);
    float range = rangeMax - rangeMin;
    // 09-11: el tramado (+-1 paso de 5 bits, 8/255, distinto en cada imagen) en zonas casi planas: el estimador de
    // movimiento del escalado empareja bloques de 8x8 por su brillo y alli da vectores al azar, y DLSS rehace la historia
    // con ellos = bloques que se mueven como en un video de poco bitrate (niebla, cuevas). Donde los 3x3 vecinos no se
    // separan mas de ~1 paso en ningun canal, la media de los 9 (como el filtro de video del N64): rampas, bordes y
    // texturas de 2 pasos o mas quedan igual. Medido sin conexion: el ruido del tramado 3,2 -> 0,65-1,0 de desviacion y
    // el cambio de una imagen a otra -59 %
    // (solo donde la cruz de 5 ya tiene poco contraste de luma: es condicion necesaria y ahorra las 9 muestras en los bordes)
    if (s2_tramado > 0 && range < 13.0 / 255.0) {
        vec3 c0 = color.rgb, sum = vec3(0.0), lo = c0, hi = c0;
        vec3 t9[9];
        for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
            vec3 t = HOOKED_texOff(vec2(float(i), float(j))).rgb;
            t9[(j + 1) * 3 + i + 1] = t;
            sum += t; lo = min(lo, t); hi = max(hi, t);
        }
        vec3 rg = hi - lo;
        float dw = 1.0 - smoothstep(9.0 / 255.0, 13.0 / 255.0, max(rg.r, max(rg.g, rg.b)));
        // 11-11: SOLO donde hay tramado de 5 bits de verdad. Las capturas reales de Ocarina (10-11) son de 8 bits sin tramado (ruido
        // de +-1 nivel) y este filtro les alisaba la textura suave: la neblina y las paredes oscuras salian planas, sin los montes,
        // arboles y grano del fondo (render de mpv de las capturas: en lo de poco contraste quedaba el 41-66 % de la textura de lo
        // oscuro y el 59-82 % de la de los medios). Primero el 3x3 que ya esta leido: sus 12 parejas de vecinos (por canal) deben ser
        // 0-1 niveles o pasos limpios de 8,25 / 16,5 (+-2) con al menos un paso; luego lo mismo en el 5x5 (40 parejas, 2 pasos o mas)
        float steps = 0.0, off = 0.0;
        for (int j = 0; j < 3; j++) for (int i = 0; i < 2; i++) {
            vec3 dh = t9[j * 3 + i + 1] - t9[j * 3 + i], dv = t9[(i + 1) * 3 + j] - t9[i * 3 + j];
            for (int c = 0; c < 3; c++) {
                float a = S2Paso4(dh[c]), b = S2Paso4(dv[c]);
                steps += max(a, 0.0) + max(b, 0.0); off += max(-a, 0.0) + max(-b, 0.0);
            }
        }
        if (dw > 0.0 && off < 0.5 && steps > 0.5) {
            // 09-11 tarde: media de 5x5, solo con los puntos a menos de ~1,5 pasos de la media de 3x3 (respeta degradados y
            // bordes suaves). MEDIDO en la salida a 4K (sin generacion, panoramica sintetica con tramado de 5 bits): cambio de
            // una imagen a otra en la zona oscura 0,194 -> 0,159 y grano 0,41 -> 0,34, sin coste de fps (grafica 58 -> 60 %)
            vec3 m3 = sum / 9.0, s5 = vec3(0.0); float n5 = 0.0;
            vec3 t5[25];
            for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++) t5[(j + 2) * 5 + i + 2] = HOOKED_texOff(vec2(float(i), float(j))).rgb;
            steps = 0.0; off = 0.0;
            for (int j = 0; j < 5; j++) for (int i = 0; i < 4; i++) {
                vec3 dh = t5[j * 5 + i + 1] - t5[j * 5 + i], dv = t5[(i + 1) * 5 + j] - t5[i * 5 + j];
                for (int c = 0; c < 3; c++) {
                    float a = S2Paso4(dh[c]), b = S2Paso4(dv[c]);
                    steps += max(a, 0.0) + max(b, 0.0); off += max(-a, 0.0) + max(-b, 0.0);
                }
            }
            if (off < 0.5 && steps > 1.5) {
                for (int k = 0; k < 25; k++) {
                    vec3 dd = abs(t5[k] - m3);
                    float w = 1.0 - smoothstep(10.0 / 255.0, 14.0 / 255.0, max(dd.r, max(dd.g, dd.b)));
                    s5 += t5[k] * w; n5 += w;
                }
                vec3 m5 = n5 > 0.5 ? s5 / n5 : m3;
                return vec4(mix(c0, m5, dw), color.a);
            }
        }
    }
    // 09-11: el umbral minimo queda por encima de un paso del tramado de 5 bits (8/255 = 0.031): con 0.0312 el
    // FXAA retocaba el 34-45 % de los puntos tramados en zonas oscuras y niebla y los apelmazaba en manchas
    // (aspecto de "poco bitrate"). Fuera tambien la limpieza de casi negros del 08-11 (aplastaba el nivel 1 a
    // negro, oscurecia 2-5 y no quitaba parpadeo medible)
    if (range < max(EDGE_THRESHOLD_MIN, rangeMax * EDGE_THRESHOLD)) return color;

    float lumaNW = S2O4(-1.0, -1.0), lumaSE = S2O4(1.0, 1.0), lumaNE = S2O4(1.0, -1.0), lumaSW = S2O4(-1.0, 1.0);
    float lumaNS = lumaN + lumaS, lumaWE = lumaW + lumaE;
    float subpixNSWE = lumaNS + lumaWE;
    float edgeHorz1 = -2.0 * lumaM + lumaNS;
    float edgeVert1 = -2.0 * lumaM + lumaWE;
    float lumaNESE = lumaNE + lumaSE;
    float lumaNWNE = lumaNW + lumaNE;
    float edgeHorz2 = -2.0 * lumaE + lumaNESE;
    float edgeVert2 = -2.0 * lumaN + lumaNWNE;
    float lumaNWSW = lumaNW + lumaSW;
    float lumaSWSE = lumaSW + lumaSE;
    float edgeHorz3 = -2.0 * lumaW + lumaNWSW;
    float edgeVert3 = -2.0 * lumaS + lumaSWSE;
    float edgeHorz = abs(edgeHorz3) + abs(edgeHorz1) * 2.0 + abs(edgeHorz2);
    float edgeVert = abs(edgeVert3) + abs(edgeVert1) * 2.0 + abs(edgeVert2);
    float subpixNWSWNESE = lumaNWSW + lumaNESE;
    float lengthSign = pt.x;
    bool horzSpan = edgeHorz >= edgeVert;
    float subpixA = subpixNSWE * 2.0 + subpixNWSWNESE;
    if (!horzSpan) { lumaN = lumaW; lumaS = lumaE; } else { lengthSign = pt.y; }
    float subpixB = subpixA / 12.0 - lumaM;
    float gradientN = lumaN - lumaM, gradientS = lumaS - lumaM;
    float lumaNN = lumaN + lumaM, lumaSS = lumaS + lumaM;
    bool pairN = abs(gradientN) >= abs(gradientS);
    float gradient = max(abs(gradientN), abs(gradientS));
    if (pairN) lengthSign = -lengthSign;
    float subpixC = clamp(abs(subpixB) / range, 0.0, 1.0);

    vec2 posB = pos;
    vec2 offNP = horzSpan ? vec2(pt.x, 0.0) : vec2(0.0, pt.y);
    if (!horzSpan) posB.x += lengthSign * 0.5; else posB.y += lengthSign * 0.5;
    vec2 posN = posB - offNP;
    vec2 posP = posB + offNP;
    float subpixD = -2.0 * subpixC + 3.0;
    float subpixE = subpixC * subpixC;
    if (!pairN) lumaNN = lumaSS;
    float gradientScaled = gradient * 0.25;
    float lumaMM = lumaM - lumaNN * 0.5;
    float subpixF = subpixD * subpixE;
    bool lumaMLTZero = lumaMM < 0.0;
    float lumaEndN = S2L4(posN) - lumaNN * 0.5;
    float lumaEndP = S2L4(posP) - lumaNN * 0.5;
    bool doneN = abs(lumaEndN) >= gradientScaled;
    bool doneP = abs(lumaEndP) >= gradientScaled;

    // Se sigue el borde a los dos lados, hasta 25 pixeles: las escaleras largas de lineas casi rectas
    const int STEPS = 8;
    float stepLen[8] = float[8](1.0, 1.5, 2.0, 2.0, 2.0, 2.0, 4.0, 8.0);
    for (int i = 0; i < STEPS && !(doneN && doneP); i++) {
        if (!doneN) {
            posN -= offNP * stepLen[i];
            lumaEndN = S2L4(posN) - lumaNN * 0.5;
            doneN = abs(lumaEndN) >= gradientScaled;
        }
        if (!doneP) {
            posP += offNP * stepLen[i];
            lumaEndP = S2L4(posP) - lumaNN * 0.5;
            doneP = abs(lumaEndP) >= gradientScaled;
        }
    }

    float dstN = horzSpan ? pos.x - posN.x : pos.y - posN.y;
    float dstP = horzSpan ? posP.x - pos.x : posP.y - pos.y;
    bool directionN = dstN < dstP;
    float dst = min(dstN, dstP);
    bool goodSpan = directionN ? ((lumaEndN < 0.0) != lumaMLTZero) : ((lumaEndP < 0.0) != lumaMLTZero);
    float pixelOffset = 0.5 - dst / (dstP + dstN);
    float pixelOffsetGood = goodSpan ? pixelOffset : 0.0;
    float subpixH = subpixF * subpixF * SUBPIX;
    float off = max(pixelOffsetGood, subpixH);
    vec2 posM = pos;
    if (!horzSpan) posM.x += off * lengthSign; else posM.y += off * lengthSign;
    return vec4(HOOKED_tex(posM).rgb, color.a);
}
