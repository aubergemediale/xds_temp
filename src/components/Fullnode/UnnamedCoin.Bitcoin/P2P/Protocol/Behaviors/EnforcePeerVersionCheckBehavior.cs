﻿using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.Utilities;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Behaviors
{
    /// <summary>
    ///     Sets the minimum supported client version <see cref="this.NodeSettings.MinProtocolVersion" /> to
    ///     <see cref="this.Network.Consensus.Options.EnforcedMinProtocolVersion" />
    ///     based on the predefined block height
    ///     <see cref="this.Network.Consensus.Options.EnforceMinProtocolVersionAtBlockHeight" />.
    ///     Once the new minimum supported client version is changed all existing peer connections will be dropped upon the
    ///     first received message from outdated client.
    /// </summary>
    public class EnforcePeerVersionCheckBehavior : NetworkPeerBehavior
    {
        /// <summary>
        ///     An indexer that provides methods to query the best chain (the chain that is validated by the full consensus
        ///     rules)
        /// </summary>
        protected readonly ChainIndexer ChainIndexer;

        /// <summary>Instance logger.</summary>
        protected readonly ILogger Logger;

        /// <summary>Logger factory used while cloning the object.</summary>
        protected readonly ILoggerFactory LoggerFactory;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        protected readonly Network Network;

        /// <summary>User defined node settings.</summary>
        protected readonly NodeSettings NodeSettings;

        /// <summary>
        ///     Set to <c>true</c> if the attached peer callbacks have been registered and they should be unregistered,
        ///     <c>false</c> if the callbacks are not registered.
        /// </summary>
        protected bool CallbacksRegistered;

        /// <summary>
        ///     Initializes an instance of the object for outbound network peers.
        /// </summary>
        /// <param name="chainIndexer">The chain of blocks.</param>
        /// <param name="nodeSettings">User defined node settings.</param>
        /// <param name="network">Specification of the network the node runs on - regtest/testnet/mainnet.</param>
        /// <param name="loggerFactory">Factory for creating loggers.</param>
        public EnforcePeerVersionCheckBehavior(ChainIndexer chainIndexer,
            NodeSettings nodeSettings,
            Network network,
            ILoggerFactory loggerFactory)
        {
            this.ChainIndexer = chainIndexer;
            this.NodeSettings = nodeSettings;
            this.Network = network;
            this.LoggerFactory = loggerFactory;
            this.Logger = loggerFactory.CreateLogger(GetType().FullName, $"[{GetHashCode():x}] ");
        }


        protected Task OnMessageReceivedAsync(INetworkPeer peer, IncomingMessage message)
        {
            var enforceMinProtocolVersionAtBlockHeight =
                this.Network.Consensus.Options.EnforceMinProtocolVersionAtBlockHeight;
            var enforcementEnabled = enforceMinProtocolVersionAtBlockHeight > 0;
            var enforcementApplied = this.NodeSettings.MinProtocolVersion >=
                                     this.Network.Consensus.Options.EnforcedMinProtocolVersion;

            var enforcementHeightReached = this.ChainIndexer.Height >= enforceMinProtocolVersionAtBlockHeight;
            if (enforcementEnabled && !enforcementApplied && enforcementHeightReached)
            {
                this.Logger.LogDebug("Changing the minumum supported protocol version from {0} to {1}.",
                    this.NodeSettings.MinProtocolVersion, this.Network.Consensus.Options.EnforcedMinProtocolVersion);
                this.NodeSettings.MinProtocolVersion = this.Network.Consensus.Options.EnforcedMinProtocolVersion;
            }

            // The statement below will close connections in case the this.NodeSettings.MinProtocolVersion has changed during node execution.
            if (peer?.PeerVersion?.Version != null && peer.PeerVersion.Version < this.NodeSettings.MinProtocolVersion)
            {
                this.Logger.LogError("Unsupported client version, dropping connection.");
                this.AttachedPeer.Disconnect("Peer is using unsupported client version");
            }

            this.Logger.LogTrace("(-)");
            return Task.CompletedTask;
        }


        protected override void AttachCore()
        {
            this.AttachedPeer.MessageReceived.Register(OnMessageReceivedAsync);
            this.CallbacksRegistered = true;
        }


        protected override void DetachCore()
        {
            if (this.CallbacksRegistered)
            {
                this.AttachedPeer.MessageReceived.Unregister(OnMessageReceivedAsync);
            }
        }


        public override object Clone()
        {
            return new EnforcePeerVersionCheckBehavior(this.ChainIndexer, this.NodeSettings, this.Network,
                this.LoggerFactory);
        }
    }
}