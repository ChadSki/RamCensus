#nowarn "9"  // Explicit struct layout has undefined behavior if fields overlap

open System
open System.Runtime.InteropServices

module Native =

    //#########################################
    //## General
    //#########################################

    type NtStatus =
         | Success = 0x0u
         | InfoLengthMismatch = 0xc0000004u

    [<Struct>]
    type UnicodeString =
         val Length : uint16
         val MaximumLength : uint16
         val buffer : IntPtr
         override m.ToString() = Marshal.PtrToStringUni(m.buffer)

    let CopyStructFromPtr<'a> ptr = Marshal.PtrToStructure(ptr, typedefof<'a>) :?> 'a

    let BytesToMegabytes numBytes = numBytes / (1024. * 1024.)

    let PagesToMegabytes numPages = let numBytes = uint32(Environment.SystemPageSize) * numPages
                                    BytesToMegabytes (float numBytes)

    //#########################################
    //## Function Signatures and Required Types
    //#########################################

    type SystemInformationClass =
         | PerformanceInformation = 0x2  // Kernel-mode memory (kernel and drivers)
         | ProcessInformation = 0x5      // User-mode memory (processes)

    [<Struct;StructLayout(LayoutKind.Explicit)>]
    type SystemMemoryInfo =
         [<FieldOffset(116)>] val NonPagedPoolPages : uint32         // non-paged (pinned) pool
         [<FieldOffset(140)>] val ResidentSystemCodePage : uint32    // system/kernel code
         [<FieldOffset(164)>] val ResidentSystemCachePage : uint32   // system/kernel cache
         [<FieldOffset(168)>] val ResidentPagedPoolPage : uint32     // paged (pageable) pool 
         [<FieldOffset(172)>] val ResidentSystemDriverPage : uint32  // driver code

    [<Struct;StructLayout(LayoutKind.Explicit)>]
    type ProcessMemoryInfo =
        [<FieldOffset(  0)>] val NextEntryOffset : uint32       // distance (bytes) to next struct
        [<FieldOffset(  8)>] val WorkingSetPrivateSize : int64  // memory owned by this process
        [<FieldOffset( 56)>] val ImageName : UnicodeString      // process name
        [<FieldOffset( 68)>] val UniqueProcessId : IntPtr       // pid
        [<FieldOffset(104)>] val WorkingSetSize : uint32        // memory used by this process
        member this.RamFootprint
            with get () = BytesToMegabytes (float this.WorkingSetPrivateSize)

    [<DllImport("ntdll.dll", SetLastError = false)>]
    extern NtStatus NtQuerySystemInformation(SystemInformationClass InfoClass, IntPtr Info, uint32 Size, uint32& outLength)

    // Call NtQSI with larger and large buffers until it works
    let QuerySystemInformation infoClass =
        let mutable cbBuffer = 1024 * 128
        let mutable pBuffer = Marshal.AllocHGlobal(cbBuffer)
        let mutable outLength = uint32(0)
        let mutable status = NtQuerySystemInformation(infoClass, pBuffer, uint32(cbBuffer), &outLength)
        while status = NtStatus.InfoLengthMismatch do
            Marshal.FreeHGlobal(pBuffer)
            cbBuffer <- cbBuffer * 2
            pBuffer <- Marshal.AllocHGlobal(cbBuffer)
            status <- NtQuerySystemInformation(infoClass, pBuffer, uint32(cbBuffer), &outLength)
        if not(status = NtStatus.Success)then
            printfn "NtQuerySystemInformation failed, NTSTATUS = %d" (uint32 status)
        pBuffer

    //#########################################
    //## Wrapper Functions
    //#########################################

    type MemoryUsage = { Name : string; MemoryUsage : float }

    // Sequence of all accounted-for memory by type/process.
    let QueryMemoryInfo () : MemoryUsage list =

        let QuerySystemMemoryInfo () =
            seq {
                let pBuffer = QuerySystemInformation SystemInformationClass.PerformanceInformation
                let perfInfo = CopyStructFromPtr<SystemMemoryInfo> pBuffer
                Marshal.FreeHGlobal(pBuffer)

                // Kernel-mode memory types and their current sizes
                yield { Name="PagedPool";    MemoryUsage=PagesToMegabytes perfInfo.ResidentPagedPoolPage }
                yield { Name="NonPagedPool"; MemoryUsage=PagesToMegabytes perfInfo.NonPagedPoolPages }
                yield { Name="SystemCache";  MemoryUsage=PagesToMegabytes perfInfo.ResidentSystemCachePage }
                yield { Name="DriverCode";   MemoryUsage=PagesToMegabytes perfInfo.ResidentSystemDriverPage }
            }

        // Sequence of user-mode processes and their current memory footprints
        let QueryProcessMemoryInfo () =
            seq {
                let pBuffer = QuerySystemInformation SystemInformationClass.ProcessInformation
                let firstInfo = CopyStructFromPtr<ProcessMemoryInfo> pBuffer
                yield { Name="SystemIdleProcess"; MemoryUsage=firstInfo.RamFootprint }

                let offset = ref firstInfo.NextEntryOffset
                let currPtr = ref pBuffer
                while not(!offset = 0u) do
                    currPtr := !currPtr + nativeint(!offset)
                    let currInfo = CopyStructFromPtr<ProcessMemoryInfo> !currPtr
                    yield { Name=currInfo.ImageName.ToString(); MemoryUsage=currInfo.RamFootprint }
                    offset := currInfo.NextEntryOffset
                Marshal.FreeHGlobal(pBuffer)
            }

        // Combine and return
        [ QuerySystemMemoryInfo (); QueryProcessMemoryInfo () ]
        |> Seq.concat
        |> Seq.toList

[<EntryPoint>]
do
    Native.QueryMemoryInfo () |> List.map(fun each -> printfn "%A" each)
                              |> ignore
    printfn "Done!"
    Console.ReadKey() |> ignore
