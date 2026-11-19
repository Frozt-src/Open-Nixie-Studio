using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

// The mobile client identifies the UART endpoints by these two UUIDs. Discover
// their roles from GATT properties so adapter and service addresses stay local.
sealed class BluetoothLink : IDisposable
{
    static readonly Guid First = new Guid("0000fff1-0000-1000-8000-00805f9b34fb");
    static readonly Guid Second = new Guid("0000fff2-0000-1000-8000-00805f9b34fb");
    readonly ConcurrentQueue<byte> input = new ConcurrentQueue<byte>();
    BluetoothLEDevice device;
    GattDeviceService service;
    GattCharacteristic reader,writer;
    bool closed, opening=true;
    readonly DateTime connectDeadline=DateTime.UtcNow.AddSeconds(30);
    volatile bool linked;
    public string Error { get; private set; }
    public bool Open { get { return linked && !closed; } }
    public bool StartHandshake
    {
        get {
            if(!Open && !closed && opening && DateTime.UtcNow>=connectDeadline)
                Error="Bluetooth connection timed out. Refresh discovery and retry.";
            if(!Open || !opening) return false;
            opening=false; return true;
        }
    }
    public BluetoothLink(ulong address) { Connect(address); }
    async void Connect(ulong address)
    {
        try {
            var found=await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if(closed) { if(found!=null) found.Dispose(); return; }
            if(found==null) throw new InvalidOperationException("Windows could not open this Bluetooth clock.");
            device=found;
            device.ConnectionStatusChanged+=LinkChanged;
            var services=await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            if(services.Status!=GattCommunicationStatus.Success) throw new InvalidOperationException("Bluetooth service discovery failed.");
            foreach(var candidate in services.Services)
            {
                if(closed || service!=null) { candidate.Dispose(); continue; }
                bool keep=false;
                try {
                    var chars=await candidate.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    if(closed || chars.Status!=GattCommunicationStatus.Success) continue;
                    GattCharacteristic rx=null,tx=null;
                    foreach(var c in chars.Characteristics)
                    {
                        if(c.Uuid!=First && c.Uuid!=Second) continue;
                        if((c.CharacteristicProperties & (GattCharacteristicProperties.Notify|GattCharacteristicProperties.Indicate))!=0) rx=c;
                        if((c.CharacteristicProperties & (GattCharacteristicProperties.Write|GattCharacteristicProperties.WriteWithoutResponse))!=0) tx=c;
                    }
                    if(rx!=null && tx!=null) { reader=rx; writer=tx; service=candidate; keep=true; }
                } finally { if(!keep) candidate.Dispose(); }
            }
            if(closed) return;
            if(reader==null) throw new InvalidOperationException("The clock UART characteristics were unavailable.");
            reader.ValueChanged+=Receive;
            var mode=(reader.CharacteristicProperties&GattCharacteristicProperties.Notify)!=0 ?
                GattClientCharacteristicConfigurationDescriptorValue.Notify : GattClientCharacteristicConfigurationDescriptorValue.Indicate;
            var status=await reader.WriteClientCharacteristicConfigurationDescriptorAsync(mode);
            if(status!=GattCommunicationStatus.Success) throw new InvalidOperationException("Could not subscribe to clock notifications.");
            linked=!closed;
        } catch(Exception e) { if(!closed) Error=e.GetBaseException().Message; }
    }
    void Receive(GattCharacteristic sender,GattValueChangedEventArgs e)
    {
        using(var r=DataReader.FromBuffer(e.CharacteristicValue))
            while(r.UnconsumedBufferLength>0) input.Enqueue(r.ReadByte());
    }
    void LinkChanged(BluetoothLEDevice sender,object args)
    {
        if(linked && sender.ConnectionStatus==BluetoothConnectionStatus.Disconnected)
            Error="Bluetooth clock disconnected.";
    }
    public bool Pop(out byte b) { return input.TryDequeue(out b); }
    // Serialize GATT writes even if notifications arrive before Windows completes a write.
    readonly Queue<byte[]> output=new Queue<byte[]>();
    bool writing;
    public async void Send(byte[] data)
    {
        output.Enqueue(data);
        if(writing) return;
        writing=true;
        try {
            while(!closed && output.Count>0)
            using(var w=new DataWriter())
            {
                w.WriteBytes(output.Dequeue());
                var option=(writer.CharacteristicProperties & GattCharacteristicProperties.Write)!=0 ?
                    GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
                var result=await writer.WriteValueAsync(w.DetachBuffer(),option);
                if(result!=GattCommunicationStatus.Success) throw new InvalidOperationException("Bluetooth write failed.");
            }
        } catch(Exception e) { if(!closed) Error=e.GetBaseException().Message; }
        finally { writing=false; }
    }
    public void Dispose()
    {
        closed=true; linked=false; output.Clear();
        if(reader!=null) reader.ValueChanged-=Receive;
        if(device!=null) device.ConnectionStatusChanged-=LinkChanged;
        if(service!=null) service.Dispose();
        if(device!=null) device.Dispose();
    }
}

sealed class BluetoothPicker : Form
{
    readonly ListBox devices=new ListBox { Dock=DockStyle.Fill };
    readonly Label message=new Label { Dock=DockStyle.Top,Height=52,Text="Scanning for XG clocks. Keep the clock nearby and disconnect other Bluetooth clients." };
    readonly Button connect=new Button { Dock=DockStyle.Bottom,Text="Connect",Enabled=false };
    readonly Dictionary<ulong,string> seen=new Dictionary<ulong,string>();
    BluetoothLEAdvertisementWatcher watcher;
    public ulong Address { get; private set; }
    public BluetoothPicker()
    {
        Text="Find a Bluetooth clock"; ClientSize=new System.Drawing.Size(460,285);
        Padding=new Padding(14); StartPosition=FormStartPosition.CenterParent;
        Controls.Add(devices); Controls.Add(message); Controls.Add(connect);
        devices.SelectedIndexChanged+=delegate { connect.Enabled=devices.SelectedIndex>=0; };
        connect.Click+=delegate {
            Address=((KeyValuePair<ulong,string>)devices.SelectedItem).Key; DialogResult=DialogResult.OK;
        };
        devices.DisplayMember="Value";
        Shown+=delegate {
            try {
                watcher=new BluetoothLEAdvertisementWatcher { ScanningMode=BluetoothLEScanningMode.Active };
                watcher.Received+=Advertisement; watcher.Stopped+=Stopped; watcher.Start();
            } catch(Exception e) { message.Text=e.GetBaseException().Message; }
        };
        FormClosed+=delegate {
            if(watcher!=null) { watcher.Received-=Advertisement; watcher.Stopped-=Stopped; watcher.Stop(); }
        };
    }
    void Post(Action action)
    {
        try { if(IsHandleCreated && !IsDisposed) BeginInvoke(action); } catch(InvalidOperationException) { }
    }
    void Stopped(BluetoothLEAdvertisementWatcher sender,BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    { Post(delegate { message.Text="Bluetooth scan stopped ("+args.Error+"). Check the Windows Bluetooth adapter."; }); }
    void Advertisement(BluetoothLEAdvertisementWatcher sender,BluetoothLEAdvertisementReceivedEventArgs e)
    {
        string name=e.Advertisement.LocalName;
        if(string.IsNullOrWhiteSpace(name) || !name.StartsWith("XG",StringComparison.OrdinalIgnoreCase)) return;
        ulong address=e.BluetoothAddress;
        Post(delegate {
            if(seen.ContainsKey(address)) return;
            seen[address]=name; devices.Items.Add(new KeyValuePair<ulong,string>(address,name));
        });
    }
}
