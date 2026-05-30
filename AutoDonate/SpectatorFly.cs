using System;
using System.Collections.Generic;
using System.Linq;
using HintServiceMeow.Core.Enum;
using HintServiceMeow.Core.Extension;
using HintServiceMeow.Core.Models.Hints;
using HintServiceMeow.Core.Utilities;
using InventorySystem.Items;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.Scp049Events;
using LabApi.Events.Arguments.Scp096Events;
using LabApi.Events.Arguments.Scp173Events;
using LabApi.Events.Arguments.Scp3114Events;
using LabApi.Events.Arguments.Scp914Events;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Features.Wrappers;
using MEC;
using Mirror;
using PlayerRoles;
using UnityEngine;

namespace AutoDonate
{
    public static class SpectatorFly
    {
        private static readonly HashSet<string> _enabled = new HashSet<string>();
        private static readonly HashSet<string> _muted = new HashSet<string>();
        private static CoroutineHandle _keepAlive;

        private static Dictionary<string, ToyInfo> _toys = new Dictionary<string, ToyInfo>();
        private static byte _nextId = 200;
        private static GameObject _toyPrefab;
        private static bool _prefabSearched = false;

        private class ToyInfo
        {
            public GameObject GameObject;
            public AdminToys.SpeakerToy Toy;
            public byte ControllerId;
        }

        private class PendingToy
        {
            public AdminToys.SpeakerToy Toy;
        }

        private static Dictionary<byte, PendingToy> _pending = new Dictionary<byte, PendingToy>();

        private const string muteHint = "autodonate_voicemute";

        private static readonly Dictionary<Faction, List<RoleTypeId>> Roles = new Dictionary<Faction, List<RoleTypeId>>()
        {
            { Faction.FoundationStaff , new List<RoleTypeId> { RoleTypeId.NtfPrivate, RoleTypeId.NtfSergeant } },
            { Faction.FoundationEnemy , new List<RoleTypeId> { RoleTypeId.ChaosConscript, RoleTypeId.ChaosRifleman, RoleTypeId.ChaosMarauder } }
        };

        private static void SetSpectatorHidden(Player player, bool hidden)
        {
            if (player == null) return;
            player.IsSpectatable = !hidden;
        }

        private static void MakeInvisible(Player player)
        {
            if (player == null) return;
            SetSpectatorHidden(player, true);
        }

        private static void MakeVisible(Player player)
        {
            if (player == null) return;
            SetSpectatorHidden(player, false);
        }

        // ===== Ghostly: постоянный эффект, пока игрок в режиме полёта =====
        private static void ApplyGhostly(Player player)
        {
            if (player?.ReferenceHub == null) return;
            // 0f = бесконечно, пока не снимем вручную
            player.ReferenceHub.playerEffectsController.EnableEffect<CustomPlayerEffects.Ghostly>(0f);
        }

        private static void RemoveGhostly(Player player)
        {
            if (player?.ReferenceHub == null) return;
            player.ReferenceHub.playerEffectsController.DisableEffect<CustomPlayerEffects.Ghostly>();
        }

        public static byte GetToyId(string userId)
        {
            ToyInfo entry;
            if (_toys.TryGetValue(userId, out entry) && entry.GameObject != null)
                return entry.ControllerId;
            return 0;
        }

        private static bool CreateToy(string userId, Player player)
        {
            if (_toys.ContainsKey(userId)) DestroyToy(userId);

            if (_toyPrefab == null && !_prefabSearched)
            {
                _toyPrefab = FindToy();
                _prefabSearched = true;
            }

            if (_toyPrefab == null)
                return false;

            Vector3 pos = player != null ? player.Position : Vector3.zero;
            GameObject instance = UnityEngine.Object.Instantiate(_toyPrefab, pos, Quaternion.identity);
            if (instance == null) return false;

            AdminToys.SpeakerToy toy = instance.GetComponent<AdminToys.SpeakerToy>();
            byte ctrlId = _nextId++;

            if (toy == null)
            {
                UnityEngine.Object.Destroy(instance);
                return false;
            }

            toy.NetworkControllerId = 0;
            toy.NetworkMinDistance = 1f;
            toy.NetworkMaxDistance = 1f;

            _pending[ctrlId] = new PendingToy { Toy = toy };
            toy.NetworkControllerId = ctrlId;

            NetworkServer.Spawn(instance);
            _toys[userId] = new ToyInfo { GameObject = instance, Toy = toy, ControllerId = ctrlId };
            return true;
        }

        private static bool DestroyToy(string userId)
        {
            ToyInfo entry;
            if (!_toys.TryGetValue(userId, out entry)) return false;

            if (entry.GameObject != null)
            {
                if (entry.GameObject.GetComponent<NetworkIdentity>() != null && NetworkServer.active)
                    NetworkServer.Destroy(entry.GameObject);
                else
                    UnityEngine.Object.Destroy(entry.GameObject);
            }

            _toys.Remove(userId);
            return true;
        }

        private static void ApplyPending()
        {
            if (_pending.Count == 0) return;

            foreach (var kvp in _pending.ToList())
            {
                var setup = kvp.Value;
                if (setup?.Toy != null)
                {
                    setup.Toy.NetworkMaxDistance = 99999f;
                    setup.Toy.NetworkMinDistance = 99999f;
                    setup.Toy.NetworkIsSpatial = false;
                    setup.Toy.NetworkVolume = 1f;
                }
                _pending.Remove(kvp.Key);
            }
        }

        private static void UpdateToy()
        {
            ApplyPending();

            foreach (var kvp in _toys.ToList())
            {
                if (kvp.Value?.GameObject == null)
                {
                    _toys.Remove(kvp.Key);
                    continue;
                }

                Player player = Player.Get(kvp.Key);
                if (player == null || player.ReferenceHub == null)
                {
                    DestroyToy(kvp.Key);
                    continue;
                }

                if (!_enabled.Contains(kvp.Key))
                {
                    DestroyToy(kvp.Key);
                    continue;
                }

                if (kvp.Value.GameObject?.transform != null)
                    kvp.Value.GameObject.transform.position = player.Position;
            }
        }

        internal static void Cleanup()
        {
            foreach (var userId in _toys.Keys.ToList())
                DestroyToy(userId);
            _toys.Clear();
            _pending.Clear();
        }

        private static GameObject FindToy()
        {
            AdminToys.SpeakerToy[] allToys = UnityEngine.Resources.FindObjectsOfTypeAll<AdminToys.SpeakerToy>();
            GameObject found = null;

            foreach (AdminToys.SpeakerToy toy in allToys)
            {
                if (toy == null || toy.gameObject == null) continue;
                bool isPrefab = toy.gameObject.scene == null || string.IsNullOrEmpty(toy.gameObject.scene.name);
                if (isPrefab)
                {
                    found = toy.gameObject;
                    break;
                }
            }

            return found;
        }

        public static void ShowHint(Player player, bool isMuted)
        {
            if (player == null) return;

            string text = isMuted
                ? "<color=#ff4444><size=30><b>Голос спектаторов: ВЫКЛ</b></size></color>"
                : "<color=#00ff00><size=30><b>Голос спектаторов: ВКЛ</b></size></color>";

            PlayerDisplay display = PlayerDisplay.Get(player);
            if (display == null) return;

            display.RemoveHint(muteHint);

            Hint hint = new Hint();
            hint.Id = muteHint;
            hint.Text = text;
            hint.YCoordinate = 800;
            hint.FontSize = 30;

            display.ShowHint(hint, 1.2f);
        }

        public static void ClearHint(Player player)
        {
            if (player == null) return;

            PlayerDisplay display = PlayerDisplay.Get(player);
            if (display != null)
                display.RemoveHint(muteHint);
        }

        public static bool SwitchMode(Player player, out string response)
        {
            if (_enabled.Contains(player.UserId))
            {
                _enabled.Remove(player.UserId);
                _muted.Remove(player.UserId);
                ClearHint(player);
                player.IsNoclipEnabled = false;
                player.IsGodModeEnabled = false;
                MakeVisible(player);
                RemoveGhostly(player);
                DestroyToy(player.UserId);
                player.SetRole(RoleTypeId.Spectator, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
                response = "Вы успешно вышли из режима полёта";
                return true;
            }

            string userId = player.UserId;
            _enabled.Add(userId);
            player.SetRole(RoleTypeId.Tutorial, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);

            Timing.CallDelayed(0.5f, () =>
            {
                Player freshPlayer = Player.Get(userId);
                if (freshPlayer == null) return;

                Ragdoll playerRagdoll = Ragdoll.List.FirstOrDefault(r => r.Nickname == freshPlayer.Nickname);
                if (playerRagdoll != null)
                    freshPlayer.Position = playerRagdoll.Position + Vector3.up;

                freshPlayer.IsGodModeEnabled = true;
                freshPlayer.IsNoclipEnabled = true;
                freshPlayer.AddItem(ItemType.Coin);
                MakeInvisible(freshPlayer);
                ApplyGhostly(freshPlayer);

                Timing.CallDelayed(0.3f, () =>
                {
                    Player p = Player.Get(userId);
                    if (p != null && _enabled.Contains(userId))
                        CreateToy(userId, p);
                });
            });

            response = "Вы успешно вошли в режим полёта";
            return true;
        }

        internal static void StartKeepAlive()
        {
            _keepAlive = Timing.RunCoroutine(KeepAliveLoop());
        }

        internal static void StopKeepAlive()
        {
            if (_keepAlive.IsRunning)
                Timing.KillCoroutines(_keepAlive);
        }

        private static IEnumerator<float> KeepAliveLoop()
        {
            while (true)
            {
                yield return Timing.WaitForSeconds(0.1f);

                if (_enabled.Count == 0) continue;

                foreach (string userId in _enabled.ToList())
                {
                    Player player = Player.Get(userId);

                    if (player == null || player.ReferenceHub == null || player.IsDestroyed)
                    {
                        _enabled.Remove(userId);
                        _muted.Remove(userId);
                        DestroyToy(userId);
                        continue;
                    }

                    SetSpectatorHidden(player, true);
                    ApplyGhostly(player);
                }

                UpdateToy();
            }
        }

        public static bool IsEnabled(Player player) => player != null && _enabled.Contains(player.UserId);
        public static bool IsMuted(Player player)   => player != null && _muted.Contains(player.UserId);

        public static bool ToggleMute(Player player)
        {
            if (player == null) return false;
            bool wasMuted = _muted.Contains(player.UserId);
            if (wasMuted) _muted.Remove(player.UserId);
            else _muted.Add(player.UserId);
            return !wasMuted;
        }

        internal static void ForceDisable(Player player)
        {
            if (player == null) return;
            string userId = player.UserId;
            if (!_enabled.Contains(userId)) return;

            _enabled.Remove(userId);
            _muted.Remove(userId);
            ClearHint(player);
            player.IsNoclipEnabled = false;
            player.IsGodModeEnabled = false;
            MakeVisible(player);
            RemoveGhostly(player);
            DestroyToy(userId);
        }

        internal static void OnPickingUpItem(PlayerPickingUpItemEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnPickingUpScp330(PlayerPickingUpScp330EventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnPickingUpAmmo(PlayerPickingUpAmmoEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnDroppingItem(PlayerDroppingItemEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnDroppingAmmo(PlayerDroppingAmmoEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }

        internal static void OnFlippedCoin(PlayerFlippedCoinEventArgs ev)
        {
            if (ev.Player == null || !IsEnabled(ev.Player)) return;

            List<Player> alive = Player.List.Where(p => p.IsAlive && p != ev.Player).ToList();
            if (!alive.TryGetRandomItem(out Player random))
            {
                ev.Player.SendHint("Живых игроков не найдено :<", 1.5f);
                return;
            }

            ev.Player.Position = random.Position + Vector3.up;
            ev.Player.SendHint(string.Format("Вы были телепортированы к игроку {0}", random.Nickname));
        }

        internal static void OnHurting(PlayerHurtingEventArgs ev)
        {
            if (IsEnabled(ev.Player)) { ev.IsAllowed = false; return; }
            if (ev.Attacker != null && IsEnabled(ev.Attacker)) ev.IsAllowed = false;
        }

        internal static void OnLeft(PlayerLeftEventArgs ev)
        {
            if (!IsEnabled(ev.Player)) return;
            _enabled.Remove(ev.Player.UserId);
            _muted.Remove(ev.Player.UserId);
            ClearHint(ev.Player);
            DestroyToy(ev.Player.UserId);
        }

        internal static void OnTogglingNoclip(PlayerTogglingNoclipEventArgs ev) { if (!ev.IsAllowed) ev.IsAllowed = IsEnabled(ev.Player); }

        internal static void OnWaveRespawning(WaveRespawningEventArgs ev)
        {
            if (ev.Wave == null) return;
            if (ev.Wave.Faction != Faction.FoundationStaff && ev.Wave.Faction != Faction.FoundationEnemy) return;

            foreach (Player player in Player.List.Where(p => _enabled.Contains(p.UserId)).ToList())
            {
                if (player == null || player.ReferenceHub == null) continue;

                ev.Roles[player] = RandomWaveRole(ev.Wave.Faction);

                string userId = player.UserId;
                _enabled.Remove(userId);
                _muted.Remove(userId);
                ClearHint(player);
                player.IsNoclipEnabled = false;
                player.IsGodModeEnabled = false;
                MakeVisible(player);
                RemoveGhostly(player);
                DestroyToy(userId);
            }
        }

        internal static void OnAddingTarget(Scp096AddingTargetEventArgs ev) { if (ev.Target != null && IsEnabled(ev.Target)) ev.IsAllowed = false; }
        internal static void OnAddingObserver(Scp173AddingObserverEventArgs ev) { if (ev.Target != null && IsEnabled(ev.Target)) ev.IsAllowed = false; }
        internal static void OnStrangleStarting(Scp3114StrangleStartingEventArgs ev) { if (ev.Target != null && IsEnabled(ev.Target)) ev.IsAllowed = false; }
        internal static void OnInteractingDoor(PlayerInteractingDoorEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnInteractingLocker(PlayerInteractingLockerEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnInteractingScp330(PlayerInteractingScp330EventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnInteractingWarheadLever(PlayerInteractingWarheadLeverEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnInteractingElevator(PlayerInteractingElevatorEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnInteractingGenerator(PlayerInteractingGeneratorEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnInteractingScp914(Scp914ActivatingEventArgs ev) { if (ev.Player != null && IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnUnlockingWarheadButton(PlayerUnlockingWarheadButtonEventArgs ev) { if (ev.Player != null && IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnInteractingShootingTarget(PlayerInteractingShootingTargetEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnUsingIntercom(PlayerUsingIntercomEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnTriggeringTesla(PlayerTriggeringTeslaEventArgs ev) { if (ev.Player != null && IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnUsingItem(PlayerUsingItemEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnThrowingItem(PlayerThrowingItemEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnPickingUpArmor(PlayerPickingUpArmorEventArgs ev) { if (IsEnabled(ev.Player)) ev.IsAllowed = false; }
        internal static void OnUnlockingGenerator(PlayerUnlockingGeneratorEventArgs ev) { if (ev.Player != null && IsEnabled(ev.Player)) ev.IsAllowed = false; }

        private static RoleTypeId RandomWaveRole(Faction waveFaction)
        {
            List<RoleTypeId> list = Roles[waveFaction];
            return list.GetRandomItem();
        }
    }
}
