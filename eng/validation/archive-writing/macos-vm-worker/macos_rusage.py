"""Read-only same-UID process accounting, with ABI independently checked against the installed public SDK."""
import ctypes
import json

names = '''user_time system_time pkg_idle_wkups interrupt_wkups pageins wired_size resident_size phys_footprint
proc_start_abstime proc_exit_abstime child_user_time child_system_time child_pkg_idle_wkups child_interrupt_wkups
child_pageins child_elapsed_abstime diskio_bytesread diskio_byteswritten cpu_time_qos_default cpu_time_qos_maintenance
cpu_time_qos_background cpu_time_qos_utility cpu_time_qos_legacy cpu_time_qos_user_initiated cpu_time_qos_user_interactive
billed_system_time serviced_system_time logical_writes lifetime_max_phys_footprint instructions cycles billed_energy
serviced_energy interval_max_phys_footprint runnable_time'''.split()

class Usage(ctypes.Structure):
    _fields_ = [('uuid', ctypes.c_ubyte * 16)] + [(name, ctypes.c_uint64) for name in names]

class Sampler:
    def __init__(self, abi):
        facts = json.loads(abi.read_text())
        assert facts['size'] == ctypes.sizeof(Usage) == 296
        assert [facts[key] for key in ['resident_offset', 'footprint_offset', 'start_offset', 'lifetime_offset']] == [
            Usage.resident_size.offset, Usage.phys_footprint.offset, Usage.proc_start_abstime.offset, Usage.lifetime_max_phys_footprint.offset]
        self.lib = ctypes.CDLL('/usr/lib/libproc.dylib', use_errno=True)
        self.lib.proc_pid_rusage.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_void_p]
        self.lib.proc_pid_rusage.restype = ctypes.c_int
        self.births = {}

    def read(self, pid):
        value = Usage()
        if self.lib.proc_pid_rusage(pid, 4, ctypes.byref(value)) != 0:
            raise ProcessLookupError(ctypes.get_errno(), 'The observed process is no longer available.', pid)
        birth = value.proc_start_abstime
        assert birth > 0 and self.births.setdefault(pid, birth) == birth, 'PID identity changed; do not sample a replacement.'
        return dict(pid=pid, start_abstime=birth, resident=value.resident_size, footprint=value.phys_footprint,
            lifetime_max_footprint=value.lifetime_max_phys_footprint, virtual_not_sampled=True,
            user_time_raw=value.user_time, system_time_raw=value.system_time)
