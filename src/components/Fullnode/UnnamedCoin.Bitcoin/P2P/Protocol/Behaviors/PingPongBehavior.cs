﻿using System;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin.Protocol;
using UnnamedCoin.Bitcoin.P2P.Peer;
using UnnamedCoin.Bitcoin.P2P.Protocol.Payloads;

namespace UnnamedCoin.Bitcoin.P2P.Protocol.Behaviors
{
    [Flags]
    public enum PingPongMode
    {
        SendPing = 1,
        RespondPong = 2,
        Both = 3
    }

    /// <summary>
    ///     The PingPongBehavior is responsible for firing ping message every PingInterval and responding with pong message,
    ///     and close the connection if the Ping has not been completed after TimeoutInterval.
    /// </summary>
    public class PingPongBehavior : NetworkPeerBehavior
    {
        /// <summary>
        ///     Set to <c>true</c> if the attached peer callbacks have been registered and they should be unregistered,
        ///     <c>false</c> if the callbacks are not registered.
        /// </summary>
        bool callbacksRegistered;

        readonly object cs = new object();
        volatile PingPayload currentPing;
        DateTimeOffset dateSent;

        PingPongMode mode;

        TimeSpan pingInterval;

        Timer pingTimeoutTimer;

        TimeSpan timeoutInterval;

        Timer timer;

        public PingPongBehavior()
        {
            this.Mode = PingPongMode.Both;
            this.TimeoutInterval =
                TimeSpan.FromMinutes(
                    20.0); // Long time, if in middle of download of a large bunch of blocks, it can takes time.
            this.PingInterval = TimeSpan.FromMinutes(2.0);
        }

        /// <summary>
        ///     Whether the behavior send Ping and respond with Pong (Default : Both)
        /// </summary>
        public PingPongMode Mode
        {
            get => this.mode;

            set
            {
                AssertNotAttached();
                this.mode = value;
            }
        }

        /// <summary>
        ///     Interval after which an unresponded Ping will result in a disconnection. (Default : 20 minutes)
        /// </summary>
        public TimeSpan TimeoutInterval
        {
            get => this.timeoutInterval;

            set
            {
                AssertNotAttached();
                this.timeoutInterval = value;
            }
        }

        /// <summary>
        ///     Interval after which a Ping message is fired after the last received Pong (Default : 2 minutes)
        /// </summary>
        public TimeSpan PingInterval
        {
            get => this.pingInterval;

            set
            {
                AssertNotAttached();
                this.pingInterval = value;
            }
        }

        public TimeSpan Latency { get; private set; }


        protected override void AttachCore()
        {
            if (this.AttachedPeer.PeerVersion != null && !PingVersion()
            ) // If not handshaked, still attach (the callback will also check version).
                return;

            this.AttachedPeer.MessageReceived.Register(OnMessageReceivedAsync);
            this.AttachedPeer.StateChanged.Register(OnStateChangedAsync);
            this.callbacksRegistered = true;

            this.timer = new Timer(Ping, null, 0, (int) this.PingInterval.TotalMilliseconds);
        }

        bool PingVersion()
        {
            var peer = this.AttachedPeer;
            return peer != null && peer.Version > ProtocolVersion.BIP0031_VERSION;
        }

        Task OnStateChangedAsync(INetworkPeer peer, NetworkPeerState oldState)
        {
            if (peer.State == NetworkPeerState.HandShaked)
                Ping(null);

            return Task.CompletedTask;
        }

        void Ping(object unused)
        {
            if (Monitor.TryEnter(this.cs))
                try
                {
                    var peer = this.AttachedPeer;

                    if (peer == null) return;
                    if (!PingVersion()) return;
                    if (peer.State != NetworkPeerState.HandShaked) return;
                    if (this.currentPing != null) return;

                    this.currentPing = new PingPayload();
                    this.dateSent = DateTimeOffset.UtcNow;
                    peer.SendMessage(this.currentPing);
                    this.pingTimeoutTimer = new Timer(PingTimeout, this.currentPing,
                        (int) this.TimeoutInterval.TotalMilliseconds, Timeout.Infinite);
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    Monitor.Exit(this.cs);
                }
        }

        /// <summary>
        ///     Send a ping asynchronously.
        /// </summary>
        public void Probe()
        {
            Ping(null);
        }

        void PingTimeout(object ping)
        {
            var peer = this.AttachedPeer;
            if (peer != null && (PingPayload) ping == this.currentPing)
                peer.Disconnect("Pong timeout for " + ((PingPayload) ping).Nonce);
        }

        async Task OnMessageReceivedAsync(INetworkPeer peer, IncomingMessage message)
        {
            if (!PingVersion())
                return;

            if (message.Message.Payload is PingPayload ping && this.Mode.HasFlag(PingPongMode.RespondPong))
                try
                {
                    await peer.SendMessageAsync(new PongPayload
                    {
                        Nonce = ping.Nonce
                    }).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }

            if (message.Message.Payload is PongPayload pong
                && this.Mode.HasFlag(PingPongMode.SendPing)
                && this.currentPing != null
                && this.currentPing.Nonce == pong.Nonce)
            {
                this.Latency = DateTimeOffset.UtcNow - this.dateSent;
                ClearCurrentPing();
            }
        }

        void ClearCurrentPing()
        {
            lock (this.cs)
            {
                this.currentPing = null;
                this.dateSent = default;
                var timeout = this.pingTimeoutTimer;
                if (timeout != null)
                {
                    timeout.Dispose();
                    this.pingTimeoutTimer = null;
                }
            }
        }


        protected override void DetachCore()
        {
            if (this.callbacksRegistered)
            {
                this.AttachedPeer.MessageReceived.Unregister(OnMessageReceivedAsync);
                this.AttachedPeer.StateChanged.Unregister(OnStateChangedAsync);
            }

            ClearCurrentPing();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            this.timer?.Dispose();

            base.Dispose();
        }


        public override object Clone()
        {
            return new PingPongBehavior();
        }
    }
}