﻿using System.Net;
using System.Net.Sockets;

namespace ReverseProxy_NET6.Proxy;

public class UdpConnection
{
  private readonly UdpClient _forwardClient;
  private readonly TaskCompletionSource<bool> _forwardConnectionBindCompleted = new();

  private readonly UdpClient _localServer;
  private readonly IPEndPoint _remoteEndpoint;
  private readonly EndPoint? _serverLocalEndpoint;
  private readonly IPEndPoint _sourceEndpoint;
  private EndPoint? _forwardLocalEndpoint;
  private bool _isRunning;
  private long _totalBytesForwarded;
  private long _totalBytesResponded;

  public UdpConnection(UdpClient localServer,
                       IPEndPoint sourceEndpoint,
                       IPEndPoint remoteEndpoint) {
    _localServer = localServer;
    _serverLocalEndpoint = _localServer.Client.LocalEndPoint;

    _isRunning = true;
    _remoteEndpoint = remoteEndpoint;
    _sourceEndpoint = sourceEndpoint;

    _forwardClient = new UdpClient(AddressFamily.InterNetworkV6);
    _forwardClient.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
  }

  public long LastActivity { get; private set; } = Environment.TickCount64;

  public async Task SendToServerAsync(byte[] message) {
    LastActivity = Environment.TickCount64;

    await _forwardConnectionBindCompleted.Task.ConfigureAwait(false);
    var sent = await _forwardClient.SendAsync(message, message.Length, _remoteEndpoint).ConfigureAwait(false);
    Interlocked.Add(ref _totalBytesForwarded, sent);
  }

  public void Run() {
    Task.Run(async () => {
      using (_forwardClient) {
        _forwardClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        _forwardLocalEndpoint = _forwardClient.Client.LocalEndPoint;
        _forwardConnectionBindCompleted.SetResult(true);
        Log.Information("[UDP] [ESTABLISHED] {SourceEndpoint} => {@ServerLocalEndpoint} => {@ForwardLocalEndpoint} => {RemoteEndpoint}",
                        _sourceEndpoint,
                        _serverLocalEndpoint,
                        _forwardLocalEndpoint,
                        _remoteEndpoint);

        while (_isRunning)
          try {
            var result = await _forwardClient.ReceiveAsync().ConfigureAwait(false);
            LastActivity = Environment.TickCount64;
            var sent = await _localServer.SendAsync(result.Buffer, result.Buffer.Length, _sourceEndpoint).ConfigureAwait(false);
            Interlocked.Add(ref _totalBytesResponded, sent);
          }
          catch (Exception ex) {
            if (_isRunning)
              Log.Error(ex, "[UDP] An exception occurred while receiving a server datagram");
          }
      }
    });
  }

  public void Stop() {
    try {
      Log.Warning("[UDP] Closed connection {SourceEndpoint} => {@ServerLocalEndpoint} => {@ForwardLocalEndpoint} => {@RemoteEndpoint}. {TotalBytesForwarded} bytes forwarded, {TotalBytesResponded} bytes responded",
                  _sourceEndpoint,
                  _serverLocalEndpoint,
                  _forwardLocalEndpoint,
                  _remoteEndpoint,
                  _totalBytesForwarded,
                  _totalBytesResponded);
      _isRunning = false;
      _forwardClient.Close();
    }
    catch (Exception ex) {
      Log.Error(ex, "[UDP] An exception occurred while closing UdpConnection");
      Console.WriteLine();
    }
  }
}