using Sandbox.Definitions;
using Sandbox.Game.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using ToolCore.Definitions;
using ToolCore.Utils;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace ToolCore.Session
{
    internal class BlockLimitInfo
    {
        internal int PCULimit;
        internal bool TrackPCU;
        internal bool TrackPlayerPCU;

        internal readonly ConcurrentDictionary<long, int> PlayerPCU = new ConcurrentDictionary<long, int>();

        private readonly Dictionary<long, int> _playerPCUTemp = new Dictionary<long, int>();

        internal void Update(MyObjectBuilder_SessionSettings gameSettings, ToolCoreSettings coreSettings)
        {
            PCULimit = gameSettings.TotalPCU;

            TrackPlayerPCU = coreSettings.RespectPlayerMaxPCU;

            TrackPCU = TrackPlayerPCU;
        }

        internal void AggregateStatsParallel()
        {
            try
            {
                var slims = ToolSession.Instance.SlimList;
                slims.ApplyChanges();

                IterateBlocks(slims);

                if (TrackPlayerPCU)
                {
                    foreach (var item in _playerPCUTemp.ToList())
                    {
                        var player = item.Key;
                        var pcu = item.Value;

                        _playerPCUTemp[player] = 0;

                        int oldValue = 0;
                        PlayerPCU.TryGetValue(player, out oldValue);
                        if (pcu == oldValue)
                            continue;


                        PlayerPCU[player] = pcu;
                    }
                }

            }
            catch (Exception ex)
            {
                Logs.LogException(ex);
            }
        }

        internal void IterateBlocks(FastLookupCachingList<IMySlimBlock> slims)
        {
            for (int i = 0; i < slims.Count; i++)
            {
                var slim = slims[i];

                var nonFunctional = slim.FatBlock != null && !slim.FatBlock.IsFunctional;
                var pcu = nonFunctional ? 1 : ((MyCubeBlockDefinition)slim.BlockDefinition).PCU;

                if (TrackPlayerPCU)
                {
                    var author = slim.BuiltBy;
                    if (!_playerPCUTemp.ContainsKey(author))
                        _playerPCUTemp[author] = 0;

                    _playerPCUTemp[author] += pcu;
                }

            }
        }
    }
}
