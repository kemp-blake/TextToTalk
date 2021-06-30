﻿using Dalamud.CrystalTower.Commands;
using Dalamud.CrystalTower.DependencyInjection;
using Dalamud.CrystalTower.UI;
using Dalamud.Game.ClientState.Actors.Types;
using Dalamud.Game.Internal;
using Dalamud.Game.Internal.Gui.Addon;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Linq;
using System.Reflection;
using System.Speech.Synthesis;
using TextToTalk.Modules;
using TextToTalk.Talk;
using TextToTalk.UI;

namespace TextToTalk
{
    public class TextToTalk : IDalamudPlugin
    {
#if DEBUG
        private const bool InitiallyVisible = true;
#else
        private const bool InitiallyVisible = false;
#endif

        private DalamudPluginInterface pluginInterface;
        private PluginConfiguration config;
        private WindowManager ui;
        private CommandManager commandManager;

        private Addon talkAddonInterface;

        private SpeechSynthesizer speechSynthesizer;
        private WsServer wsServer;
        private SharedState sharedState;

        private PluginServiceCollection serviceCollection;

        public string Name => "TextToTalk";

        public void Initialize(DalamudPluginInterface pi)
        {
            this.pluginInterface = pi;

            this.config = (PluginConfiguration)this.pluginInterface.GetPluginConfig() ?? new PluginConfiguration();
            this.config.Initialize(this.pluginInterface);

            this.wsServer = new WsServer();
            this.speechSynthesizer = new SpeechSynthesizer();
            this.sharedState = new SharedState();

            this.serviceCollection = new PluginServiceCollection();
            this.serviceCollection.AddService(this.config);
            this.serviceCollection.AddService(this.wsServer);
            this.serviceCollection.AddService(this.sharedState);
            this.serviceCollection.AddService(this.speechSynthesizer);
            this.serviceCollection.AddService(this.pluginInterface, shouldDispose: false);

            this.ui = new WindowManager(this.serviceCollection);
            this.serviceCollection.AddService(this.ui, shouldDispose: false);

            this.ui.AddWindow<UnlockerResultWindow>(initiallyVisible: false);
            this.ui.AddWindow<VoiceUnlockerWindow>(initiallyVisible: false);
            this.ui.AddWindow<PresetModificationWindow>(initiallyVisible: false);
            this.ui.AddWindow<ConfigurationWindow>(InitiallyVisible);

            this.pluginInterface.UiBuilder.OnBuildUi += this.ui.Draw;
            this.pluginInterface.UiBuilder.OnOpenConfigUi += OpenConfigUi;

            this.pluginInterface.Framework.Gui.Chat.OnChatMessage += OnChatMessage;

            this.pluginInterface.Framework.OnUpdateEvent += PollTalkAddon;
            this.pluginInterface.Framework.OnUpdateEvent += CheckKeybindPressed;
            this.pluginInterface.Framework.OnUpdateEvent += CheckPresetKeybindPressed;

            this.commandManager = new CommandManager(pi, this.serviceCollection);
            this.commandManager.AddCommandModule<MainCommandModule>();
        }

        private bool keysDown;
        private void CheckKeybindPressed(Framework framework)
        {
            if (!this.config.UseKeybind) return;

            if (this.pluginInterface.ClientState.KeyState[(byte)this.config.ModifierKey] &&
                this.pluginInterface.ClientState.KeyState[(byte)this.config.MajorKey])
            {
                if (this.keysDown) return;

                this.keysDown = true;

                var commandModule = this.commandManager.GetCommandModule<MainCommandModule>();
                commandModule.ToggleTts();

                return;
            }

            this.keysDown = false;
        }

        private void CheckPresetKeybindPressed(Framework framework)
        {
            foreach (var preset in this.config.EnabledChatTypesPresets.Where(p => p.UseKeybind))
            {
                if (this.pluginInterface.ClientState.KeyState[(byte)preset.ModifierKey] &&
                    this.pluginInterface.ClientState.KeyState[(byte)preset.MajorKey])
                {
                    this.config.SetCurrentEnabledChatTypesPreset(preset.Id);
                }
            }
        }

        private unsafe void PollTalkAddon(Framework framework)
        {
            if (!this.config.Enabled) return;
            if (!this.config.ReadFromQuestTalkAddon) return;

            if (this.talkAddonInterface == null || this.talkAddonInterface.Address == IntPtr.Zero)
            {
                this.talkAddonInterface = this.pluginInterface.Framework.Gui.GetAddonByName("Talk", 1);
                return;
            }

            var talkAddon = (AddonTalk*)this.talkAddonInterface.Address.ToPointer();
            if (talkAddon == null) return;

            var talkAddonText = TalkUtils.ReadTalkAddon(this.pluginInterface.Data, talkAddon);
            var text = talkAddonText.Text;

            if (talkAddonText.Text == "" || IsDuplicateQuestText(talkAddonText.Text)) return;
            SetLastQuestText(text);

#if DEBUG
            PluginLog.Log($"NPC text found: \"{text}\"");
#endif

            if (talkAddonText.Speaker != "" && ShouldSaySender())
            {
                if (!this.config.DisallowMultipleSay || !IsSameSpeaker(talkAddonText.Speaker))
                {
                    text = $"{talkAddonText.Speaker} says {text}";
                    SetLastSpeaker(talkAddonText.Speaker);
                }
            }

            var speaker = this.pluginInterface.ClientState.Actors
                .FirstOrDefault(actor => actor.Name == talkAddonText.Speaker);

            Say(speaker, text);
        }

        private void OnChatMessage(XivChatType type, uint id, ref SeString sender, ref SeString message, ref bool handled)
        {
            if (!this.config.Enabled) return;

            var textValue = message.TextValue;
            if (IsDuplicateQuestText(textValue)) return;

#if DEBUG
            PluginLog.Log("Chat message from type {0}: {1}", type, textValue);
#endif

            if (sender != null && sender.TextValue != string.Empty)
            {
                if (ShouldSaySender(type))
                {
                    if (!this.config.DisallowMultipleSay || !IsSameSpeaker(sender.TextValue))
                    {
                        if ((int)type == (int)AdditionalChatTypes.Enum.NPCDialogue)
                        {
                            SetLastQuestText(textValue);
                        }

                        textValue = $"{sender.TextValue} says {textValue}";
                        SetLastSpeaker(sender.TextValue);
                    }
                }
            }

            if (this.config.Bad.Where(t => t.Text != "").Any(t => t.Match(textValue))) return;

            var chatTypes = this.config.GetCurrentEnabledChatTypesPreset();

            var typeAccepted = chatTypes.EnabledChatTypes.Contains((int)type);
            var goodMatch = this.config.Good
                .Where(t => t.Text != "")
                .Any(t => t.Match(textValue));
            if (!(chatTypes.EnableAllChatTypes || typeAccepted) || this.config.Good.Count > 0 && !goodMatch) return;

            var playerPayload = sender?.Payloads.FirstOrDefault(payload => payload is PlayerPayload) as PlayerPayload;
            var speaker = this.pluginInterface.ClientState.Actors
                .FirstOrDefault(a => a.Name == playerPayload?.DisplayedName);

            Say(speaker, textValue);
        }

        private void Say(Actor speaker, string textValue)
        {
            var cleanText = TalkUtils.StripSSMLTokens(textValue);

            if (this.config.UseWebsocket)
                SayWebSocket(speaker, cleanText);
            else if (speaker != null && this.config.UseGenderedVoicePresets)
                SayGendered(speaker, cleanText);
            else
                SayNotGendered(cleanText);
        }

        private void SayWebSocket(Actor speaker, string cleanText)
        {
            this.wsServer.Broadcast(GetActorGender(speaker), cleanText);
#if DEBUG
            PluginLog.Log("Sent message {0} on WebSocket server.", cleanText);
#endif
        }

        private void SayGendered(Actor speaker, string cleanText)
        {
            var voicePreset = GetActorGender(speaker) switch
            {
                Gender.Male => this.config.GetCurrentMaleVoicePreset(),
                Gender.Female => this.config.GetCurrentFemaleVoicePreset(),
                _ => this.config.GetCurrentVoicePreset(),
            };

            this.speechSynthesizer.UseVoicePreset(voicePreset);
            this.speechSynthesizer.SpeakAsync(cleanText);
        }

        private void SayNotGendered(string cleanText)
        {
            var voicePreset = this.config.GetCurrentVoicePreset();
            this.speechSynthesizer.UseVoicePreset(voicePreset);
            this.speechSynthesizer.SpeakAsync(cleanText);
        }

        private static Gender GetActorGender(Actor actor)
        {
            var actorStructProp = typeof(Actor)
                .GetProperty("ActorStruct", BindingFlags.NonPublic | BindingFlags.Instance);
            if (actorStructProp == null)
            {
                PluginLog.Warning("Failed to retrieve actor struct accessor.");
                return Gender.None;
            }

            var actorStruct = (Dalamud.Game.ClientState.Structs.Actor)actorStructProp.GetValue(actor);
            var actorGender = (Gender)actorStruct.Customize[1];

            return actorGender;
        }

        private void OpenConfigUi(object sender, EventArgs args)
        {
            this.ui.ShowWindow<ConfigurationWindow>();
        }

        private bool IsDuplicateQuestText(string text)
        {
            return this.sharedState.LastQuestText == text;
        }

        private void SetLastQuestText(string text)
        {
            this.sharedState.LastQuestText = text;
        }

        private bool IsSameSpeaker(string speaker)
        {
            return this.sharedState.LastSpeaker == speaker;
        }

        private void SetLastSpeaker(string speaker)
        {
            this.sharedState.LastSpeaker = speaker;
        }

        private bool ShouldSaySender()
        {
            return this.config.EnableNameWithSay && this.config.NameNpcWithSay;
        }

        private bool ShouldSaySender(XivChatType type)
        {
            return this.config.EnableNameWithSay && (this.config.NameNpcWithSay || (int)type != (int)AdditionalChatTypes.Enum.NPCDialogue);
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            this.commandManager.Dispose();

            this.pluginInterface.Framework.OnUpdateEvent -= PollTalkAddon;
            this.pluginInterface.Framework.OnUpdateEvent -= CheckKeybindPressed;

            this.pluginInterface.Framework.Gui.Chat.OnChatMessage -= OnChatMessage;

            this.wsServer.Stop();

            this.pluginInterface.SavePluginConfig(this.config);

            this.pluginInterface.UiBuilder.OnOpenConfigUi -= OpenConfigUi;
            this.pluginInterface.UiBuilder.OnBuildUi -= this.ui.Draw;

            this.ui.Dispose();

            this.serviceCollection.Dispose();

            this.pluginInterface.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
