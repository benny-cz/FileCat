/* Included only by the development managed-worker build. No libc or shell. */
.p2align 2
managed_worker:
    // Root init already attached this child and dropped all UID/GID/groups.
    // A UID change clears PDEATHSIG, so apply it after the drop.
    mov x0, #1
    mov x1, #9
    mov x2, #0
    mov x3, #0
    mov x4, #0
    mov x8, #167
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x8, #173
    svc #0
    cmp x0, #1
    b.ne fatal
    mov x9, #30
    str x9, [x19, #128]
    str x9, [x19, #136]
    mov x0, #0
    mov x1, #0
    add x2, x19, #128
    mov x3, #0
    mov x8, #261
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x0, #-100
    adr x1, managed_tty
    mov x2, #0x102
    mov x8, #56
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x20, x0
    mov x1, #0x5401
    add x2, x19, #256
    mov x8, #29
    svc #0
    cmp x0, #0
    b.lt fatal
    str wzr, [x19, #256]
    str wzr, [x19, #260]
    str wzr, [x19, #268]
    strb wzr, [x19, #278]
    mov w9, #1
    strb w9, [x19, #279]
    mov x0, x20
    mov x1, #0x5402
    add x2, x19, #256
    mov x8, #29
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x0, x20
    mov x1, #0
    mov x2, #0
    mov x8, #24
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x0, x20
    mov x1, #1
    mov x2, #0
    mov x8, #24
    svc #0
    cmp x0, #1
    b.ne fatal
    mov x0, x20
    mov x8, #57
    svc #0
    mov x0, #-100
    adr x1, managed_cancel_tty
    mov x2, #0x100
    mov x8, #56
    svc #0
    cmp x0, #3
    b.ne fatal
    mov x20, x0
    mov x1, #0x5401
    add x2, x19, #256
    mov x8, #29
    svc #0
    cmp x0, #0
    b.lt fatal
    str wzr, [x19, #256]
    str wzr, [x19, #260]
    str wzr, [x19, #268]
    strb wzr, [x19, #278]
    mov w9, #1
    strb w9, [x19, #279]
    mov x0, x20
    mov x1, #0x5402
    add x2, x19, #256
    mov x8, #29
    svc #0
    cmp x0, #0
    b.lt fatal
    adr x0, managed_executable
    str x0, [x19, #512]
    adr x9, managed_argument
    str x9, [x19, #520]
    str xzr, [x19, #528]
    adr x9, managed_invariant
    str x9, [x19, #544]
    adr x9, managed_diagnostics
    str x9, [x19, #552]
    str xzr, [x19, #560]
    add x1, x19, #512
    add x2, x19, #544
    mov x8, #221
    svc #0
    b fatal
managed_tty: .asciz "/dev/hvc1"
managed_cancel_tty: .asciz "/dev/hvc2"
#ifndef FILECAT_GUEST_EXECUTABLE
#define FILECAT_GUEST_EXECUTABLE "/worker/FileCat.ArchiveVmProbe"
#define FILECAT_GUEST_ARGUMENT "guest"
#endif
managed_executable: .asciz FILECAT_GUEST_EXECUTABLE
managed_argument: .asciz FILECAT_GUEST_ARGUMENT
managed_invariant: .asciz "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1"
managed_diagnostics: .asciz "DOTNET_EnableDiagnostics=0"
