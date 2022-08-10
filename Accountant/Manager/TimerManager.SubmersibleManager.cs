using System;
using System.Diagnostics.CodeAnalysis;
using Accountant.Classes;
using Accountant.Enums;
using Accountant.Gui.Timer;
using Accountant.Structs;
using Accountant.Timers;
using Accountant.Util;
using Dalamud.Hooking;
using Dalamud.Logging;
using Dalamud.Utility.Signatures;

namespace Accountant.Manager;

public partial class TimerManager
{
    private sealed class SubmersibleManager : ITimerManager
    {
        public ConfigFlags RequiredFlags
            => ConfigFlags.Enabled | ConfigFlags.Submersibles;

        private readonly FreeCompanyStorage _companyStorage;

        private bool _state;

        private readonly SubmersibleTimers _submersibles;
        private readonly AirshipTimers     _airships;

        public SubmersibleManager(SubmersibleTimers submersibles, AirshipTimers airships, FreeCompanyStorage companyStorage)
        {
            SignatureHelper.Initialise(this);
            _submersibles   = submersibles;
            _airships       = airships;
            _companyStorage = companyStorage;

            SetState();
        }

        public TimerWindow.BaseCache CreateCache(TimerWindow window)
            => new TimerWindow.MachineCache(window, RequiredFlags, "Submersibles", MachineType.Submersible, _airships, _submersibles);

        public void SetState()
        {
            if (Accountant.Config.Flags.Check(RequiredFlags))
                Enable();
            else
                Disable();
        }

        private void Enable()
        {
            if (_state)
                return;

            _submersibleTimersHook?.Enable();
            _submersibleStatusListHook?.Enable();
            _submersibles.Reload();
            _state = true;
        }

        private void Disable()
        {
            if (!_state)
                return;

            _submersibleTimersHook?.Disable();
            _submersibleStatusListHook?.Disable();
            _state = false;
        }

        public void Dispose()
        {
            Disable();
            _submersibleTimersHook?.Dispose();
            _submersibleStatusListHook?.Dispose();
        }

        private delegate void PacketHandler(IntPtr manager, IntPtr data);

        [Signature("E8 E8 03 00 00 B0 01 48 81 C4 10 01 00 00 5B C3",
            DetourName = nameof(SubmersibleTimersDetour))]
        private Hook<PacketHandler>? _submersibleTimersHook = null!;

        [Signature("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 0F 10 02 4C 8D 81",
            DetourName = nameof(SubmersibleStatusListDetour))]
        private Hook<PacketHandler>? _submersibleStatusListHook = null!;

        private bool FreeCompanyInfo([NotNullWhen(true)] ref FreeCompanyInfo? info)
        {
            if (info != null)
                return true;

            info = _companyStorage.GetCurrentCompanyInfo();
            if (info != null)
                return true;

            PluginLog.Error("Could not log submersibles, unable to obtain free company name.");
            return false;
        }

        private unsafe void SubmersibleTimersDetour(IntPtr manager, IntPtr data)
        {
            try
            {
                FreeCompanyInfo? info    = null;
                var              changes = false;
                var              timer   = (SubmersibleTimer*)data;
                for (byte i = 0; i < 4; ++i)
                {
                    if (timer[i].RawName[0] == 0)
                        break;

                    if (!FreeCompanyInfo(ref info))
                        return;

                    changes |= _submersibles.AddOrUpdateSubmersible(info.Value,
                        new MachineInfo(timer[i].Name, timer[i].Date, MachineType.Submersible),
                        i);
                }

                if (changes)
                    _submersibles.Save(info!.Value);
            }
            finally

            {
                _submersibleTimersHook!.Original(manager, data);
            }
        }

        private unsafe void SubmersibleStatusListDetour(IntPtr manager, IntPtr data)
        {
            try
            {
                FreeCompanyInfo? info    = null;
                var              changes = false;
                var              status  = (SubmersibleStatus*)data;
                for (byte i = 0; i < 4; ++i)
                {
                    if (status[i].RawName[0] == 0)
                        break;

                    if (!FreeCompanyInfo(ref info))
                        return;

                    changes |= _submersibles.AddOrUpdateSubmersible(info.Value,
                        new MachineInfo(status[i].Name, status[i].Date, MachineType.Submersible), i);
                }

                if (changes)
                    _submersibles.Save(info!.Value);
            }
            finally
            {
                _submersibleStatusListHook!.Original(manager, data);
            }
        }
    }
}
