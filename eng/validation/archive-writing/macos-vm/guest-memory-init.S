/* FileCat-owned AArch64 Linux API probe, no libc or third-party implementation. */
#ifndef FILECAT_ALLOCATION_CHUNKS
#define FILECAT_ALLOCATION_CHUNKS 12
#endif
#ifndef FILECAT_LIMIT_MIB
#define FILECAT_LIMIT_MIB 64
#endif
#if FILECAT_LIMIT_MIB != 64 && FILECAT_LIMIT_MIB != 1024
#error Unsupported fixed experiment limit
#endif
.text
.global _start
.type _start, %function
_start:
    mov x0, #0
    mov x1, #4096
    mov x2, #3
    mov x3, #0x22
    mov x4, #-1
    mov x5, #0
    mov x8, #222
    svc #0
    mov x19, x0
    mov x0, #-100
    adr x1, module_path
    mov x2, #0
    mov x8, #56
    svc #0
    mov x20, x0
    adr x1, empty
    mov x2, #0
    mov x8, #273
    svc #0
    mov x24, x0
    mov x0, x20
    mov x8, #57
    svc #0
    mov x0, #0
    mov x8, #57
    svc #0
    mov x0, #1
    mov x8, #57
    svc #0
    mov x0, #2
    mov x8, #57
    svc #0
    mov x0, #-100
    adr x1, console_path
    mov x2, #2
    mov x8, #56
    svc #0
    cmp x0, #0
    b.ne fatal
    mov x0, #0
    mov x1, #1
    mov x2, #0
    mov x8, #24
    svc #0
    mov x0, #0
    mov x1, #2
    mov x2, #0
    mov x8, #24
    svc #0
    adr x0, ready
    mov x1, #(ready_end-ready)
    bl puts
    mov x0, x24
    bl hex
    cmp x24, #0
    b.lt fatal
    adr x0, proc_type
    adr x1, proc_path
    adr x2, proc_type
    mov x3, #0
    mov x4, #0
    mov x8, #40
    svc #0
    cmp x0, #0
    b.lt fatal
    adr x0, sys_type
    adr x1, sys_path
    adr x2, sys_type
    mov x3, #0
    mov x4, #0
    mov x8, #40
    svc #0
    cmp x0, #0
    b.lt fatal
    adr x0, cg_type
    adr x1, cg_path
    adr x2, cg_type
    mov x3, #0
    mov x4, #0
    mov x8, #40
    svc #0
    cmp x0, #0
    b.lt fatal
    adr x0, cg_subtree
    adr x1, cg_enable
    mov x2, #(cg_enable_end-cg_enable)
    bl open_write
    mov x0, #-100
    adr x1, cg_leaf
    mov x2, #0x1c0
    mov x8, #34
    svc #0
    cmp x0, #0
    b.lt fatal
    adr x0, cg_max
    adr x1, limit_value
    mov x2, #(limit_value_end-limit_value)
    bl open_write
    adr x0, cg_swap
    adr x1, zero
    mov x2, #1
    bl open_write
    adr x0, cg_pids
    adr x1, two
    mov x2, #1
    bl open_write
    adr x0, limit_label
    mov x1, #(limit_label_end-limit_label)
    bl puts
    adr x0, cg_max
    bl read_dump
    mov x0, #17
    mov x1, #0
    mov x2, #0
    mov x3, #0
    mov x4, #0
    mov x8, #220
    svc #0
    cmp x0, #0
    b.lt fatal
    b.eq child
    mov x20, x0
    mov x1, x19
    mov x2, #0
    mov x3, #0
    mov x8, #260
    svc #0
    cmp x0, x20
    b.ne fatal
    ldr w21, [x19]
    adr x0, wait_label
    mov x1, #(wait_label_end-wait_label)
    bl puts
    mov x0, x21
    bl hex
    adr x0, events_label
    mov x1, #(events_label_end-events_label)
    bl puts
    adr x0, cg_events
    bl read_dump
    adr x0, peak_label
    mov x1, #(peak_label_end-peak_label)
    bl puts
    adr x0, cg_peak
    bl read_dump
    adr x0, finished_label
    mov x1, #(finished_label_end-finished_label)
    bl puts
    b poweroff
child:
    adr x0, cg_procs
    adr x1, zero
    mov x2, #1
    bl open_write
    mov x0, #0
    mov x1, #0
    mov x8, #159
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x0, #1000
    mov x8, #144
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x0, #1000
    mov x8, #146
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x0, #38
    mov x1, #1
    mov x2, #0
    mov x3, #0
    mov x4, #0
    mov x8, #167
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x8, #174
    svc #0
    cmp x0, #1000
    b.ne fatal
    mov x0, #-100
    adr x1, cg_max
    mov x2, #1
    mov x8, #56
    svc #0
    cmn x0, #13
    b.ne fatal
    adr x0, escape_denied
    mov x1, #(escape_denied_end-escape_denied)
    bl puts
    mov x21, #FILECAT_ALLOCATION_CHUNKS
    mov x23, #0
allocation_loop:
    mov x0, #0
    mov x1, #0x800000
    mov x2, #3
    mov x3, #0x22
    mov x4, #-1
    mov x5, #0
    mov x8, #222
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x22, x0
    mov x9, #0x100000
    mov x10, #0x1357
fill_loop:
    eor x10, x10, x10, lsl #13
    eor x10, x10, x10, lsr #7
    eor x10, x10, x10, lsl #17
    str x10, [x22], #8
    subs x9, x9, #1
    b.ne fill_loop
    add x23, x23, #0x800000
    mov x0, x23
    bl hex
    subs x21, x21, #1
    b.ne allocation_loop
    adr x0, unenforced
    mov x1, #(unenforced_end-unenforced)
    bl puts
    mov x0, #0
    mov x8, #93
    svc #0
fatal:
    mov x25, x0
    adr x0, error_label
    mov x1, #(error_label_end-error_label)
    bl puts
    mov x0, x25
    bl hex
poweroff:
    mov x0, #0xdead
    movk x0, #0xfee1, lsl #16
    mov x1, #0x1969
    movk x1, #0x2812, lsl #16
    mov x2, #0xfedc
    movk x2, #0x4321, lsl #16
    mov x8, #142
    svc #0
    mov x0, #111
    mov x8, #93
    svc #0
    b .
puts:
    mov x2, x1
    mov x1, x0
    mov x0, #1
    mov x8, #64
    svc #0
    ret
hex:
    mov x13, x0
    adr x14, digits
    mov x15, #60
    mov x16, #0
hex_loop:
    lsr x17, x13, x15
    and x17, x17, #15
    ldrb w17, [x14, x17]
    strb w17, [x19, x16]
    add x16, x16, #1
    subs x15, x15, #4
    b.ge hex_loop
    mov w17, #10
    strb w17, [x19, #16]
    mov x0, #1
    mov x1, x19
    mov x2, #17
    mov x8, #64
    svc #0
    ret
open_write:
    mov x9, x0
    mov x10, x1
    mov x11, x2
    mov x0, #-100
    mov x1, x9
    mov x2, #1
    mov x8, #56
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x12, x0
    mov x1, x10
    mov x2, x11
    mov x8, #64
    svc #0
    cmp x0, x11
    b.ne fatal
    mov x0, x12
    mov x8, #57
    svc #0
    ret
read_dump:
    mov x1, x0
    mov x0, #-100
    mov x2, #0
    mov x8, #56
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x12, x0
    mov x1, x19
    mov x2, #512
    mov x8, #63
    svc #0
    cmp x0, #0
    b.lt fatal
    mov x2, x0
    mov x1, x19
    mov x0, #1
    mov x8, #64
    svc #0
    mov x0, x12
    mov x8, #57
    svc #0
    ret
digits: .ascii "0123456789abcdef"
module_path: .asciz "/virtio_mmio.module"
console_path: .asciz "/dev/console"
empty: .asciz ""
proc_type: .asciz "proc"
proc_path: .asciz "/proc"
sys_type: .asciz "sysfs"
sys_path: .asciz "/sys"
cg_type: .asciz "cgroup2"
cg_path: .asciz "/sys/fs/cgroup"
cg_leaf: .asciz "/sys/fs/cgroup/filecat"
cg_subtree: .asciz "/sys/fs/cgroup/cgroup.subtree_control"
cg_max: .asciz "/sys/fs/cgroup/filecat/memory.max"
cg_swap: .asciz "/sys/fs/cgroup/filecat/memory.swap.max"
cg_pids: .asciz "/sys/fs/cgroup/filecat/pids.max"
cg_procs: .asciz "/sys/fs/cgroup/filecat/cgroup.procs"
cg_events: .asciz "/sys/fs/cgroup/filecat/memory.events"
cg_peak: .asciz "/sys/fs/cgroup/filecat/memory.peak"
cg_enable: .ascii "+memory +pids"
cg_enable_end:
#if FILECAT_LIMIT_MIB == 64
limit_value: .ascii "67108864"
#else
limit_value: .ascii "1073741824"
#endif
limit_value_end:
zero: .ascii "0"
two: .ascii "2"
ready: .ascii "FILECAT_GUEST_READY module_status="
ready_end:
limit_label: .ascii "FILECAT_LIMIT="
limit_label_end:
escape_denied: .ascii "FILECAT_LIMIT_RAISE_DENIED uid=1000\n"
escape_denied_end:
wait_label: .ascii "FILECAT_WAIT_STATUS="
wait_label_end:
events_label: .ascii "FILECAT_MEMORY_EVENTS\n"
events_label_end:
peak_label: .ascii "FILECAT_MEMORY_PEAK="
peak_label_end:
finished_label: .ascii "FILECAT_GUEST_FINISHED\n"
finished_label_end:
error_label: .ascii "FILECAT_ERROR="
error_label_end:
#if FILECAT_ALLOCATION_CHUNKS * 8 <= FILECAT_LIMIT_MIB
unenforced: .ascii "FILECAT_CONTROL_COMPLETED\n"
#else
unenforced: .ascii "FILECAT_LIMIT_NOT_ENFORCED\n"
#endif
unenforced_end:
