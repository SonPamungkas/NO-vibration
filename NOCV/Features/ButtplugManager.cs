using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ButtplugManaged;
using UnityEngine;

namespace NOCV.Features;

/// <summary>
/// Connects to Intiface Central and distributes vibration commands to devices.
/// </summary>
public class ButtplugManager : MonoBehaviour
{
    public static ButtplugManager? Instance { get; private set; }
    
    private ButtplugClient? _client;
    private readonly List<ButtplugClientDevice> _devices = new();
    private readonly object _devicesLock = new(); // Thread safety for devices collection
    
    public float currentSpeed = 0f;
    private float _timeSinceUpdate;

    public static void Initialize()
    {
        if (Instance != null) return;
        var go = new GameObject("ButtplugManager");
        Instance = go.AddComponent<ButtplugManager>();
        DontDestroyOnLoad(go);
    }

    private void Start()
    {
        Task.Run(ConnectClient);
    }

    private async Task ConnectClient()
    {
        if (_client != null)
        {
            _client.DeviceAdded -= OnDeviceAdded;
            _client.DeviceRemoved -= OnDeviceRemoved;
            if (_client.IsScanning) await _client.StopScanningAsync();
            if (_client.Connected) await _client.DisconnectAsync();
        }

        _client = new ButtplugClient("Nuclear Option Buttplug");
        _client.DeviceAdded += OnDeviceAdded;
        _client.DeviceRemoved += OnDeviceRemoved;
        
        _client.ErrorReceived += (sender, args) => 
            NOCV.Logger.LogError($"Buttplug Error: {args.Exception.Message}");
            
        _client.ServerDisconnect += (sender, args) => 
        {
            NOCV.Logger.LogWarning("Buttplug server disconnected.");
            lock (_devicesLock) { _devices.Clear(); }
        };

        try
        {
            var uri = new Uri(PluginConfig.IntifaceUri.Value + "/buttplug");
            NOCV.Logger.LogInfo($"Connecting to Intiface server at {uri}...");
            
            await _client.ConnectAsync(new ButtplugWebsocketConnectorOptions(uri));
            await _client.StartScanningAsync();
            
            NOCV.Logger.LogInfo("Successfully connected to Intiface server and started scanning.");
        }
        catch (Exception ex)
        {
            NOCV.Logger.LogError($"Failed to connect to Intiface: {ex.Message}");
        }
    }

    private void OnDeviceAdded(object sender, DeviceAddedEventArgs args)
    {
        NOCV.Logger.LogInfo($"Device connected: {args.Device.Name}");
        lock (_devicesLock)
        {
            _devices.Add(args.Device);
        }
    }

    private void OnDeviceRemoved(object sender, DeviceRemovedEventArgs args)
    {
        NOCV.Logger.LogInfo($"Device disconnected: {args.Device.Name}");
        lock (_devicesLock)
        {
            _devices.Remove(args.Device);
        }
    }

    private void Update()
    {
        if (_client == null || !_client.Connected) return;

        _timeSinceUpdate += Time.deltaTime;
        
        // Use a ~10Hz tick rate to avoid flooding Bluetooth devices with commands
        if (_timeSinceUpdate > 0.1f)
        {
            float strength = Mathf.Clamp01(currentSpeed * PluginConfig.ButtplugStrength.Value);
            
            lock (_devicesLock)
            {
                foreach (var device in _devices)
                {
                    if (device.AllowedMessages.ContainsKey("VibrateCmd"))
                    {
                        device.SendVibrateCmd(strength);
                    }
                }
            }
            
            _timeSinceUpdate = 0f;
        }
    }

    private void OnDestroy()
    {
        if (_client != null && _client.Connected)
        {
            Task.Run(() => _client.DisconnectAsync());
        }
    }
}
