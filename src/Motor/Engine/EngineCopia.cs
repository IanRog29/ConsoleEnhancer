using System;
using ConsoleEnhancer.Motor.Native;

namespace ConsoleEnhancer.Motor
{
    // 16-11 (1440p con generacion): la imagen de la captura pasa a la memoria de la grafica con el motor de copia (DMA, en su propia
    // cola: va a la vez que lo que la grafica aun hace de la imagen anterior, p. ej. la generacion) y la descompresion lee de alli.
    // Antes el shader de descompresion leia la memoria de subida (RAM) por PCIe: medido 1,9-2,0 ms de la cola de la imagen a 1440p
    // (11 MB a ~5,8 GB/s) y 1,0 ms a 1080p, y a 1440p con la generacion no cabia. copia_asincrona=0: como antes.
    // Sincronizacion: copia en la cola de copia -> valla -> la cola de la imagen espera esa valla antes de su lista. Los bufferes de la
    // grafica estan en COMMON (los bufferes pasan solos a COPY_DEST en la copia y a lectura en la imagen, y vuelven a COMMON al acabar
    // cada lista). Una ranura no se vuelve a copiar hasta que su imagen ha terminado (slotFence): sin carreras con la lectura.
    unsafe partial class Engine
    {
        ID3D12CommandQueue copyQueue;
        readonly ID3D12CommandAllocator[] copyAlloc = new ID3D12CommandAllocator[FramesInFlight];
        ID3D12GraphicsCommandList copyList;
        ID3D12Fence copyFence;
        ulong copyFenceValue;
        readonly ID3D12Resource[] vramIn = new ID3D12Resource[Slots];
        bool copyOn;                // la cola de copia esta lista y se usa
        ulong copyWaitValue;        // la valla de copia de la imagen que se graba (0 = sin copia)

        // Tras InitDevice
        void InitCopy()
        {
            if (!s.AsyncCopy) { Log.Write("Captura: la descompresion lee de la memoria de subida (copia_asincrona=0)"); return; }
            try
            {
                object o;
                D3D12_COMMAND_QUEUE_DESC qd = new D3D12_COMMAND_QUEUE_DESC();
                qd.Type = D3D12_COMMAND_LIST_TYPE.COPY;
                qd.Priority = 100; // HIGH (como la de la imagen)
                Guid iid = typeof(ID3D12CommandQueue).GUID;
                Hr.Check(dev.CreateCommandQueue(ref qd, ref iid, out o), "CreateCommandQueue copia");
                copyQueue = (ID3D12CommandQueue)o;
                iid = typeof(ID3D12CommandAllocator).GUID;
                for (int i = 0; i < FramesInFlight; i++)
                {
                    Hr.Check(dev.CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.COPY, ref iid, out o), "CreateCommandAllocator copia");
                    copyAlloc[i] = (ID3D12CommandAllocator)o;
                }
                iid = typeof(ID3D12GraphicsCommandList).GUID;
                Hr.Check(dev.CreateCommandList(0, D3D12_COMMAND_LIST_TYPE.COPY, copyAlloc[0], null, ref iid, out o), "CreateCommandList copia");
                copyList = (ID3D12GraphicsCommandList)o;
                Hr.Check(copyList.Close(), "Close copia");
                iid = typeof(ID3D12Fence).GUID;
                Hr.Check(dev.CreateFence(0, D3D12_FENCE_FLAGS.NONE, ref iid, out o), "CreateFence copia");
                copyFence = (ID3D12Fence)o;
                copyOn = true;
                Log.Write("Captura: a la grafica con el motor de copia (cola propia) y descompresion desde la memoria de la grafica");
            }
            catch (Exception ex)
            {
                Log.Error("Cola de copia (se lee de la memoria de subida, como antes)", ex);
                copyOn = false;
            }
        }

        // EnsureInputResources, al (re)crear las ranuras: el buffer de la grafica de la ranura y su vista para la descompresion.
        // false = sin copia asincrona (la vista va a la memoria de subida, como antes)
        bool SlotVram(int i, long size)
        {
            if (!copyOn) return false;
            if (vramIn[i] != null) Com.Release(ref vramIn[i]);
            vramIn[i] = MakeBuffer(D3D12_HEAP_TYPE.DEFAULT, size, D3D12_RESOURCE_STATES.COMMON, "captura en la grafica " + i);
            SrvRaw(TUnpack + i, 0, vramIn[i], size);
            return true;
        }

        // Bucle de pintado, con la ranura elegida y su lista libre (tras WaitFence(allocFence[fi])): la copia ya, en su cola
        void CopySlot(int slot, int fi)
        {
            copyWaitValue = 0;
            if (!copyOn || slot < 0 || vramIn[slot] == null) return;
            int h = slotH[slot];
            long bytes = (long)slotPitch[slot] * (slotFmt[slot] == Shaders.FmtNv12 ? h + h / 2 : h);
            if (bytes <= 0 || bytes > slotBytes) return;
            Hr.Check(copyAlloc[fi].Reset(), "Allocator.Reset copia");
            Hr.Check(copyList.Reset(copyAlloc[fi], null), "List.Reset copia");
            copyList.CopyBufferRegion(vramIn[slot], 0, upload[slot], 0, (ulong)bytes);
            Hr.Check(copyList.Close(), "Close copia");
            IntPtr lp = Com.BorrowPtr(copyList, typeof(ID3D12CommandList));
            copyQueue.ExecuteCommandLists(1, ref lp);
            copyFenceValue++;
            copyQueue.Signal(copyFence, copyFenceValue);
            copyWaitValue = copyFenceValue;
        }

        // RenderFrame, justo antes de enviar su lista: la cola de la imagen espera a la copia de esta imagen
        void CopyWait()
        {
            if (copyWaitValue == 0) return;
            queue.Wait(copyFence, copyWaitValue);
            copyWaitValue = 0;
        }

        // En espera (ReleaseSessionResources): fuera con las ranuras (al despertar se rehacen con EnsureInputResources)
        void ReleaseCopySlots()
        {
            for (int i = 0; i < Slots; i++) if (vramIn[i] != null) Com.Release(ref vramIn[i]);
        }
    }
}
