using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AriadGSM.Agent.Desktop;

internal sealed partial class AgentRuntime
{
    private const string RuntimeGovernorStateFileName = "runtime-governor-state.json";
    private WindowsJobObject? _runtimeJob;
    private readonly List<OwnedProcessRecord> _ownedProcessRecords = [];

    private void StartRuntimeGovernor()
    {
        _runtimeJob?.Dispose();
        _runtimeJob = WindowsJobObject.TryCreate($"AriadGSM-Agent-{Environment.ProcessId}");
        WriteRuntimeGovernorState("ok", "Runtime Governor iniciado.", verifiedStopped: false);
    }

    private void RegisterOwnedProcess(string name, Process process, string role)
    {
        var jobAssigned = false;
        if (_runtimeJob is not null)
        {
            jobAssigned = _runtimeJob.TryAssign(process, out var error);
            if (!jobAssigned && !string.IsNullOrWhiteSpace(error))
            {
                WriteLogNoThrow($"Runtime Governor could not assign {name} pid={process.Id} to Job Object: {error}");
            }
        }

        lock (_gate)
        {
            _ownedProcessRecords.RemoveAll(item => item.Pid == process.Id);
            _ownedProcessRecords.Add(new OwnedProcessRecord(
                name,
                process.Id,
                role,
                Owned: true,
                Running: !process.HasExited,
                JobAssigned: jobAssigned,
                StartedAt: DateTimeOffset.UtcNow));
        }

        WriteRuntimeGovernorState("ok", $"{name} registrado como proceso propio.", verifiedStopped: false);
    }

    private void MarkOwnedProcessStopped(int pid)
    {
        lock (_gate)
        {
            var index = _ownedProcessRecords.FindIndex(item => item.Pid == pid);
            if (index >= 0)
            {
                var current = _ownedProcessRecords[index];
                _ownedProcessRecords[index] = current with { Running = false };
            }
        }
    }

    private void StopRuntimeGovernor(string reason)
    {
        WriteRuntimeGovernorState("attention", $"Apagado solicitado: {reason}.", verifiedStopped: false);
        _runtimeJob?.Dispose();
        _runtimeJob = null;
        ReconcileOwnedProcessRecordsAfterStop();
        WriteRuntimeGovernorState("idle", $"Apagado verificado: {reason}.", verifiedStopped: true);
    }

    private void ReconcileOwnedProcessRecordsAfterStop()
    {
        lock (_gate)
        {
            for (var index = 0; index < _ownedProcessRecords.Count; index++)
            {
                var record = _ownedProcessRecords[index];
                var running = IsPidRunning(record.Pid);
                _ownedProcessRecords[index] = record with { Running = running };
            }
        }
    }

    private void WriteRuntimeGovernorState(string status, string headline, bool verifiedStopped)
    {
        try
        {
            OwnedProcessRecord[] records;
            lock (_gate)
            {
                records = _ownedProcessRecords.ToArray();
            }

            var runningOwned = records.Count(item => item.Owned && item.Running);
            var orphanedOwned = !_desiredRunning ? runningOwned : 0;
            var state = new Dictionary<string, object?>
            {
                ["status"] = status,
                ["engine"] = "ariadgsm_runtime_governor",
                ["version"] = CurrentVersion,
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["contract"] = "runtime_governor_state",
                ["runSessionId"] = CurrentRunSessionIdNoLock(),
                ["desiredRunning"] = _desiredRunning,
                ["policy"] = new Dictionary<string, object?>
                {
                    ["windowsJobObject"] = _runtimeJob?.Available ?? false,
                    ["killBrowsers"] = false,
                    ["gracefulShutdownFirst"] = true,
                    ["forceKillOwnedOnly"] = true,
                    ["controlLoop"] = "desired_state_vs_observed_state"
                },
                ["ownedProcesses"] = records.Select(item => new Dictionary<string, object?>
                {
                    ["name"] = item.Name,
                    ["pid"] = item.Pid,
                    ["role"] = item.Role,
                    ["owned"] = item.Owned,
                    ["running"] = item.Running,
                    ["jobAssigned"] = item.JobAssigned,
                    ["startedAt"] = item.StartedAt
                }).ToArray(),
                ["summary"] = new Dictionary<string, object?>
                {
                    ["ownedTotal"] = records.Count(item => item.Owned),
                    ["runningOwned"] = runningOwned,
                    ["orphanedOwned"] = orphanedOwned,
                    ["forcedStops"] = 0,
                    ["browsersObservedNotOwned"] = 0,
                    ["verifiedStopped"] = verifiedStopped && runningOwned == 0
                },
                ["humanReport"] = new Dictionary<string, object?>
                {
                    ["headline"] = headline,
                    ["queEstaPasando"] = new[]
                    {
                        $"{runningOwned} procesos AriadGSM propios vivos.",
                        "Edge, Chrome y Firefox no son propiedad del Runtime Governor."
                    },
                    ["riesgos"] = orphanedOwned > 0
                        ? new[] { "Hay procesos AriadGSM vivos aunque el estado deseado es detenido." }
                        : Array.Empty<string>()
                }
            };

            WriteAllTextAtomicShared(
                Path.Combine(_runtimeDir, RuntimeGovernorStateFileName),
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static bool IsPidRunning(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private sealed record OwnedProcessRecord(
        string Name,
        int Pid,
        string Role,
        bool Owned,
        bool Running,
        bool JobAssigned,
        DateTimeOffset StartedAt);

    private sealed class WindowsJobObject : IDisposable
    {
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private IntPtr _handle;

        private WindowsJobObject(IntPtr handle)
        {
            _handle = handle;
        }

        public bool Available => _handle != IntPtr.Zero;

        public static WindowsJobObject? TryCreate(string name)
        {
            var handle = CreateJobObject(IntPtr.Zero, name);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var info = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                }
            };

            var length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            var pointer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, pointer, fDeleteOld: false);
                if (!SetInformationJobObject(handle, JobObjectInfoType.ExtendedLimitInformation, pointer, (uint)length))
                {
                    CloseHandle(handle);
                    return null;
                }

                return new WindowsJobObject(handle);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        public bool TryAssign(Process process, out string error)
        {
            error = string.Empty;
            if (_handle == IntPtr.Zero)
            {
                error = "Job Object no disponible.";
                return false;
            }

            if (AssignProcessToJobObject(_handle, process.Handle))
            {
                return true;
            }

            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr hJob,
            JobObjectInfoType jobObjectInformationClass,
            IntPtr lpJobObjectInformation,
            uint cbJobObjectInformationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private enum JobObjectInfoType
        {
            ExtendedLimitInformation = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public long Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
