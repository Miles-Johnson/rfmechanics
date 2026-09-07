using System;
using System.Collections.Generic;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace rfmechanics;

public partial class DwarfOreSongModSystem : ModSystem
{
    private ICoreServerAPI? sapi;
    private IServerNetworkChannel? serverChannel;
    private DwarfOreSongIndex? index;
    private RFMechanicsConfig serverConfig = new();
    private readonly List<ListeningSession> listeners = new();
    private readonly Dictionary<string, long> nextRequest = new();
    private readonly Dictionary<string, long> nextListen = new();
    private long serverTickId;
    private int roundRobin;
    private double worstSliceMs;
    private long totalSlices, incompleteAnswers;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.Network.RegisterChannel(OreSongRules.Channel)
            .RegisterMessageType<OreSongRequest>().RegisterMessageType<OreSongReply>();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        // Own server configuration snapshot: the old global static is shared with the client
        // in singleplayer. Clients never get to choose range, budgets or eligibility.
        try { serverConfig = api.LoadModConfig<RFMechanicsConfig>("rfmechanics.json") ?? new(); }
        catch (Exception error) { api.Logger.Warning("[rfmechanics] Ore-Song using defaults: {0}", error.Message); }
        serverChannel = api.Network.GetChannel(OreSongRules.Channel).SetMessageHandler<OreSongRequest>(OnRequest);
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
        {
            index = new DwarfOreSongIndex(api, serverConfig.OreSongCacheChunks);
            api.Event.ChunkDirty += index.Invalidate;
        });
        api.Event.PlayerDisconnect += OnDisconnect;
        serverTickId = api.Event.RegisterGameTickListener(OnServerTick, OreSongRules.TickMs);
        api.ChatCommands.Create("rforesong")
            .WithDescription("Read Ore-Song server work and cache counters; does not scan or change terrain.")
            .RequiresPrivilege(Privilege.controlserver)
            .HandleWith(_ => TextCommandResult.Success(
                $"Ore-Song: {listeners.Count} listeners; {index?.CacheCount ?? 0} cached chunks; " +
                $"{index?.BlocksRead ?? 0} block reads, {index?.PaletteSkips ?? 0} palette skips, " +
                $"{index?.CacheHits ?? 0} cache hits; {totalSlices} work ticks; " +
                $"worst work slice {worstSliceMs:F2}ms (target {BudgetMs:F2}ms); " +
                $"{incompleteAnswers} incomplete answers. Loaded terrain only."));
    }

    private double BudgetMs => double.IsFinite(serverConfig.OreSongServerBudgetMs)
        ? Math.Clamp(serverConfig.OreSongServerBudgetMs, 0.25, 2) : 1;
    private int SettleMs => Math.Clamp(serverConfig.OreSongSettleMs, 1000, 5000);
    private int ListenMs => Math.Clamp(serverConfig.OreSongListenMs, 10000, 20000);
    private int RestMs => Math.Clamp(serverConfig.OreSongRestMs, 1000, 10000);

    private void Reply(IServerPlayer player, int id, string state)
        => serverChannel?.SendPacket(new OreSongReply { Id = id, State = state, ListenMs = ListenMs, RestMs = RestMs }, player);

    private void OnRequest(IServerPlayer player, OreSongRequest request)
    {
        if (sapi == null) return;
        long now = sapi.World.ElapsedMilliseconds;
        int existing = listeners.FindIndex(s => s.Player.PlayerUID == player.PlayerUID);
        if (request.Cancel)
        {
            if (existing >= 0 && listeners[existing].Id == request.Id) EndSession(existing, now, true);
            return;
        }
        if (nextRequest.TryGetValue(player.PlayerUID, out long next) && now < next) return;
        nextRequest[player.PlayerUID] = now + 500;
        if (!serverConfig.DwarfOreSongEnabled) { Reply(player, request.Id, "disabled"); return; }
        if (index == null || existing >= 0 || (nextListen.TryGetValue(player.PlayerUID, out next) && now < next)
            || listeners.Count >= Math.Clamp(serverConfig.OreSongMaxListeners, 1, 8))
        {
            Reply(player, request.Id, "busy");
            return;
        }
        EntityPlayer entity = player.Entity;
        var wall = new BlockPos(request.X, request.Y, request.Z, entity.Pos.Dimension);
        if (!OreSongRules.IsSeated(entity, true) || !RaceTraits.HasTrait(player, serverConfig.DwarfTraitCode)
            || !OreSongRules.InReach(entity, wall, true) || !OreSongRules.IsStone(sapi.World.BlockAccessor.GetBlock(wall))
            || !CanTouchWall(entity, wall))
        {
            Reply(player, request.Id, "invalid");
            return;
        }
        int radius = Math.Clamp(serverConfig.OreSongListeningRadius, 16, 96);
        listeners.Add(new ListeningSession
        {
            Player = player, Id = request.Id, Wall = wall, Origin = entity.Pos.XYZ,
            StartedMs = now, Query = index.CreateQuery(wall, radius)
        });
        Reply(player, request.Id, "settling");
    }

    private bool CanTouchWall(EntityPlayer entity, BlockPos wall)
    {
        if (sapi == null) return false;
        var from = new Vec3d(entity.Pos.X, entity.Pos.InternalY + entity.LocalEyePos.Y, entity.Pos.Z);
        var to = new Vec3d(wall.X + 0.5, wall.InternalY + 0.5, wall.Z + 0.5);
        BlockSelection? selected = null;
        EntitySelection? selectedEntity = null;
        sapi.World.RayTraceForSelection(from, to, ref selected, ref selectedEntity);
        return selected?.Position.Equals(wall) == true;
    }

    private bool StillListening(ListeningSession session)
    {
        EntityPlayer entity = session.Player.Entity;
        return sapi != null && entity.Pos.Dimension == session.Wall.dimension
            && OreSongRules.IsSeated(entity, true) && entity.Pos.XYZ.SquareDistanceTo(session.Origin) <= OreSongRules.MoveToleranceSq
            && RaceTraits.HasTrait(session.Player, serverConfig.DwarfTraitCode)
            && OreSongRules.IsStone(sapi.World.BlockAccessor.GetBlock(session.Wall));
    }

    private void OnServerTick(float dt)
    {
        if (sapi == null || index == null || listeners.Count == 0) return;
        long now = sapi.World.ElapsedMilliseconds;
        for (int i = listeners.Count - 1; i >= 0; i--)
        {
            var session = listeners[i];
            if (!serverConfig.DwarfOreSongEnabled || !StillListening(session)) { EndSession(i, now, true); continue; }
            if (session.AnsweredMs > 0 && now - session.AnsweredMs >= ListenMs) EndSession(i, now, false);
        }
        if (listeners.Count == 0) return;

        var timer = Stopwatch.StartNew();
        int newChunks = 0, quanta = 0, idle = 0;
        try
        {
            // Global caps, not per player: <= 1ms target/50ms by default, <= 32768 block
            // reads, <= 8 new chunk preparations. Slow machines do less work, not longer ticks.
            while (timer.Elapsed.TotalMilliseconds < BudgetMs && newChunks < 8 && quanta < 128 && idle < listeners.Count)
            {
                roundRobin %= listeners.Count;
                var session = listeners[roundRobin++];
                if (session.AnsweredMs > 0 || session.Query.Done) { idle++; continue; }
                idle = 0;
                if (index.Step(session.Query, now)) newChunks++;
                quanta++;
            }
        }
        catch (Exception error)
        {
            sapi.Logger.Error("[rfmechanics] Ore-Song search cancelled: {0}", error);
            index.Clear();
            for (int i = listeners.Count - 1; i >= 0; i--) EndSession(i, now, true);
        }
        timer.Stop();
        if (quanta > 0) { totalSlices++; worstSliceMs = Math.Max(worstSliceMs, timer.Elapsed.TotalMilliseconds); }

        foreach (var session in listeners)
        {
            if (session.AnsweredMs > 0 || now - session.StartedMs < SettleMs) continue;
            if (!session.Query.Done && now - session.StartedMs < OreSongRules.MaxSearchMs) continue;
            var foundVoices = session.Query.Voices();
            bool incomplete = !session.Query.Done || session.Query.Incomplete;
            if (incomplete) incompleteAnswers++;
            session.AnsweredMs = now;
            serverChannel?.SendPacket(new OreSongReply
            {
                Id = session.Id, State = "answer", Incomplete = incomplete,
                ListenMs = ListenMs, RestMs = RestMs, Voices = foundVoices,
                MaxVoices = Math.Clamp(serverConfig.OreSongMaxClusters, 1, 3)
            }, session.Player);
            // Ordinary knock audible to companions; ore responses remain private to the dwarf.
            sapi.World.PlaySoundAt(new AssetLocation("rfmechanics", "sounds/oresong/knock.ogg"),
                session.Wall.X + 0.5, session.Wall.InternalY + 0.5, session.Wall.Z + 0.5,
                session.Player, false, 12, 0.65f);
        }
    }

    private void EndSession(int position, long now, bool cancelled)
    {
        var session = listeners[position];
        nextListen[session.Player.PlayerUID] = now + RestMs;
        if (cancelled) Reply(session.Player, session.Id, "cancelled");
        listeners.RemoveAt(position);
    }

    private void OnDisconnect(IServerPlayer player)
    {
        listeners.RemoveAll(s => s.Player.PlayerUID == player.PlayerUID);
        nextRequest.Remove(player.PlayerUID);
        nextListen.Remove(player.PlayerUID);
    }

    public override void Dispose()
    {
        if (sapi != null)
        {
            sapi.Event.UnregisterGameTickListener(serverTickId);
            sapi.Event.PlayerDisconnect -= OnDisconnect;
            if (index != null) sapi.Event.ChunkDirty -= index.Invalidate;
        }
        index?.Clear();
        listeners.Clear();
        nextRequest.Clear();
        nextListen.Clear();
        DisposeClient();
        base.Dispose();
    }

    private sealed class ListeningSession
    {
        internal IServerPlayer Player = null!;
        internal int Id;
        internal BlockPos Wall = null!;
        internal Vec3d Origin = null!;
        internal long StartedMs, AnsweredMs;
        internal DwarfOreSongIndex.Query Query = null!;
    }
}
