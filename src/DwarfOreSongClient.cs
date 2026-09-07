using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace rfmechanics;

public partial class DwarfOreSongModSystem
{
    private ICoreClientAPI? capi;
    private IClientNetworkChannel? clientChannel;
    private long clientTickId, requestedMs, answerMs, readyMs;
    private int requestId, phrase, playedVoices, listenMs = 10000, restMs = 3000;
    private bool pending, answered, waitingNotice;
    private BlockPos? clientWall;
    private Vec3d? clientOrigin;
    private OreSongVoice[] voices = Array.Empty<OreSongVoice>();
    private int voicesPerPhrase = 3;
    private float playbackVolume = 1;
    private bool captions;
    private readonly List<ILoadedSound> playing = new();

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        clientChannel = api.Network.GetChannel(OreSongRules.Channel).SetMessageHandler<OreSongReply>(OnReply);
        try
        {
            var config = api.LoadModConfig<RFMechanicsConfig>("rfmechanics.json") ?? new();
            playbackVolume = float.IsFinite(config.OreSongVolume) ? Math.Clamp(config.OreSongVolume, 0, 2) : 1;
            captions = config.OreSongCaptions;
        }
        catch { /* Core config loader already reports malformed config; audio uses safe defaults. */ }
        clientTickId = api.Event.RegisterGameTickListener(OnClientTick, OreSongRules.TickMs);
    }

    internal bool TryTrigger(ICoreClientAPI api)
    {
        if (clientChannel == null || !clientChannel.Connected) { Hint("unavailable"); return true; }
        EntityPlayer? entity = api.World.Player?.Entity;
        if (entity == null) return false;
        if (pending || answered) return true;
        long now = api.World.ElapsedMilliseconds;
        if (now < readyMs) { Hint("rest"); return true; }
        BlockPos? wall = api.World.Player?.CurrentBlockSelection?.Position;
        if (!OreSongRules.IsSeated(entity, false) || wall == null || !OreSongRules.InReach(entity, wall, false)
            || !OreSongRules.IsStone(api.World.BlockAccessor.GetBlock(wall)))
        {
            Hint("prepare");
            return true;
        }
        StopSounds();
        clientWall = wall.Copy();
        clientOrigin = entity.Pos.XYZ;
        pending = true;
        answered = false;
        waitingNotice = false;
        requestedMs = now;
        requestId = requestId == int.MaxValue ? 1 : requestId + 1;
        clientChannel.SendPacket(new OreSongRequest { Id = requestId, X = wall.X, Y = wall.Y, Z = wall.Z });
        Hint("settle");
        return true;
    }

    private void OnReply(OreSongReply reply)
    {
        if (capi == null || reply.Id != requestId || (!pending && !answered)) return;
        listenMs = Math.Clamp(reply.ListenMs, 10000, 20000);
        restMs = Math.Clamp(reply.RestMs, 1000, 10000);
        if (reply.State == "settling") return;
        if (reply.State != "answer")
        {
            EndClient(false);
            Hint(reply.State);
            return;
        }
        if (!pending || !ClientStillListening()) { EndClient(true); return; }
        pending = false;
        answered = true;
        answerMs = capi.World.ElapsedMilliseconds;
        voices = reply.Voices ?? Array.Empty<OreSongVoice>();
        voicesPerPhrase = Math.Clamp(reply.MaxVoices, 1, 3);
        playedVoices = 0;
        // Rotate through remaining minerals on subsequent knocks. Individual bearings stay stable.
        if (voices.Length > 0) phrase %= voices.Length;
        capi.World.Player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
        Play("knock", null, 0.65f, 1, 1);
        Play("stone", null, 0.16f, 1, 1);
        if (reply.Incomplete) Hint("incomplete");
        else if (voices.Length == 0) Hint("empty");
    }

    private bool ClientStillListening()
    {
        EntityPlayer? entity = capi?.World.Player?.Entity;
        return entity != null && clientOrigin != null && clientWall != null
            && entity.Pos.Dimension == clientWall.dimension && OreSongRules.IsSeated(entity, false)
            && entity.GetBehavior<PlayerRaceBehavior>()?.Race == PlayerRace.Dwarf
            && entity.Pos.XYZ.SquareDistanceTo(clientOrigin) <= OreSongRules.MoveToleranceSq
            && OreSongRules.IsStone(capi!.World.BlockAccessor.GetBlock(clientWall));
    }

    private void OnClientTick(float dt)
    {
        if (capi == null) return;
        // DisposeOnFinish owns natural completion; keep only live handles for cancellation.
        playing.RemoveAll(sound => sound.IsDisposed);
        if (!pending && !answered) return;
        if (!ClientStillListening()) { EndClient(true); return; }
        long now = capi.World.ElapsedMilliseconds;
        if (pending)
        {
            if (!waitingNotice && now - requestedMs > 3500) { Hint("waiting"); waitingNotice = true; }
            if (now - requestedMs > OreSongRules.MaxSearchMs + 5000) { EndClient(true); Hint("unavailable"); }
            return;
        }
        int count = Math.Min(voicesPerPhrase, voices.Length);
        if (playedVoices < count && now - answerMs >= 1000 + playedVoices * 2000)
        {
            PlayVoice(voices[(phrase + playedVoices) % voices.Length]);
            playedVoices++;
        }
        if (now - answerMs >= listenMs)
        {
            phrase += count;
            EndClient(false);
        }
    }

    private void PlayVoice(OreSongVoice voice)
    {
        if (capi == null) return;
        string asset = OreSongRules.AssetFor(voice.Material);
        // Packet never supplies arbitrary file paths; map material locally as well.
        int band = Math.Clamp(voice.DistanceBand, 0, 3);
        float volume = band switch { 0 => 0.38f, 1 => 0.34f, 2 => 0.27f, _ => 0.21f };
        float clarity = band switch { 0 => 1, 1 => 0.9f, 2 => 0.7f, _ => 0.5f };
        float grade = Math.Clamp(voice.Grade, 0, 1);
        int variation = capi.World.Rand.Next(3);
        float pitch = 0.99f + (float)capi.World.Rand.NextDouble() * 0.02f;
        Play($"{asset}-{variation}-rough", voice, volume * (1 - grade), pitch, clarity);
        Play($"{asset}-{variation}-clear", voice, volume * grade, pitch, clarity);
        // More ore supplies a modest second voice, not unlimited amplitude or source count.
        Play($"{asset}-{(variation + 1) % 3}-{(grade >= 0.5 ? "clear" : "rough")}", voice,
            volume * 0.22f * Math.Clamp(voice.Fullness, 0, 1), 2 - pitch, clarity);
        if (captions)
        {
            string direction = band == 0 ? Lang.Get("rfmechanics:oresong-around")
                : Lang.Get("rfmechanics:oresong-bearing-" + ((voice.Bearing % 12 + 12) % 12));
            string elevation = Lang.Get("rfmechanics:oresong-elevation-" + Math.Clamp(voice.Elevation, -1, 1));
            capi.ShowChatMessage(Lang.Get("rfmechanics:oresong-caption",
                Lang.Get("rfmechanics:oresong-voice-" + asset), direction, elevation,
                Lang.Get("rfmechanics:oresong-distance-" + band),
                Lang.Get("rfmechanics:oresong-fullness-" + (voice.Fullness < 0.4f ? 0 : voice.Fullness < 0.8f ? 1 : 2)),
                Lang.Get("rfmechanics:oresong-grade-" + (grade < 0.3f ? 0 : grade < 0.7f ? 1 : 2))));
        }
    }

    private void Play(string asset, OreSongVoice? voice, float volume, float pitch, float clarity)
    {
        if (capi == null || volume * playbackVolume < 0.001f) return;
        bool surrounding = voice == null || voice.DistanceBand == 0;
        Vec3f position = new(0, 0, 0);
        if (!surrounding && clientOrigin != null && voice != null)
        {
            double angle = voice.Bearing * Math.PI / 6;
            // A virtual source encodes the coarse bearing. Using the real 96-block distance
            // would introduce a second, hardware-dependent attenuation curve and reveal precision.
            var entity = capi.World.Player.Entity;
            position.Set((float)(clientOrigin.X + Math.Cos(angle) * 4),
                (float)(clientOrigin.Y + entity.LocalEyePos.Y + voice.Elevation * 2),
                (float)(clientOrigin.Z + Math.Sin(angle) * 4));
        }
        ILoadedSound? sound = capi.World.LoadSound(new SoundParams
        {
            Location = new AssetLocation("rfmechanics", "sounds/oresong/" + asset + ".ogg"),
            Position = position, RelativePosition = surrounding, ShouldLoop = false,
            DisposeOnFinish = true, SoundType = EnumSoundType.Sound, Pitch = pitch,
            Volume = Math.Clamp(volume * playbackVolume, 0, 1), ReferenceDistance = 8, Range = 32
        });
        if (sound == null) return;
        sound.SetLowPassfiltering(clarity);
        sound.Start();
        playing.Add(sound);
    }

    private void Hint(string key)
    {
        if (capi == null) return;
        string text = Lang.Get("rfmechanics:oresong-" + key);
        if (key is "settle" or "waiting" or "empty" or "incomplete" or "cancelled")
            capi.TriggerIngameDiscovery(this, "oresong", text);
        else capi.TriggerIngameError(this, "oresong", text);
    }

    private void EndClient(bool cancel)
    {
        if (cancel && (pending || answered)) clientChannel?.SendPacket(new OreSongRequest { Id = requestId, Cancel = true });
        pending = answered = false;
        readyMs = (capi?.World.ElapsedMilliseconds ?? 0) + restMs;
        voices = Array.Empty<OreSongVoice>();
        StopSounds();
    }

    private void StopSounds()
    {
        foreach (var sound in playing)
        {
            if (sound.IsDisposed) continue;
            sound.Stop();
            sound.Dispose();
        }
        playing.Clear();
    }

    private void DisposeClient()
    {
        if (capi != null) capi.Event.UnregisterGameTickListener(clientTickId);
        StopSounds();
        pending = answered = false;
        capi = null;
    }
}
