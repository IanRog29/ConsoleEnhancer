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
// 11-11: quitar el tramado solo donde hay tramado de 5 bits de verdad (ver abajo); en un juego de 8 bits queda el FXAA solo

//!PARAM s2_tramado
//!DESC 1 = quitar el tramado en las zonas casi planas (niebla, cielos, sombras); 0 = no (ini quitar_tramado)
//!TYPE int
//!MINIMUM 0
//!MAXIMUM 1
1

//!HOOK MAIN
//!BIND HOOKED
//!DESC S2 AA

#define EDGE_THRESHOLD 0.125 // 08-11: punto medio (0.09/0.02/0.5 emborronaba la hierba; 0.166/0.0625/0.25 daba +10 % de parpadeo con la generacion)
#define EDGE_THRESHOLD_MIN 0.047 // 09-11: 0.0312 encendia el FXAA sobre el tramado oscuro (ver abajo)
#define SUBPIX 0.25 // 09-11: 0.375 emborronaba detalle de 1 pixel que DLSS ya no recupera

float S2L(vec2 p) { return dot(HOOKED_tex(p).rgb, vec3(0.299, 0.587, 0.114)); }
float S2O(float x, float y) { return dot(HOOKED_texOff(vec2(x, y)).rgb, vec3(0.299, 0.587, 0.114)); }
float S2Paso(float d) { // diferencia entre vecinos: 1 = paso de 5 bits (8,25 o 16,5 +-2 niveles), 0 = igual (0-1), -1 = otra cosa
    float ad = abs(d) * 255.0;
    if (ad < 2.0) return 0.0;
    float e = abs(ad - 8.25 * floor(ad / 8.25 + 0.5));
    return (e < 2.0 && ad < 20.0) ? 1.0 : -1.0;
}

vec4 hook() {
    vec4 color = HOOKED_texOff(vec2(0.0, 0.0));
    vec2 pos = HOOKED_pos;
    vec2 pt = HOOKED_pt;
    float lumaM = dot(color.rgb, vec3(0.299, 0.587, 0.114));
    float lumaS = S2O(0.0, 1.0), lumaE = S2O(1.0, 0.0), lumaN = S2O(0.0, -1.0), lumaW = S2O(-1.0, 0.0);
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
                float a = S2Paso(dh[c]), b = S2Paso(dv[c]);
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
                    float a = S2Paso(dh[c]), b = S2Paso(dv[c]);
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
