using System;
using System.IO.Ports;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;

sealed class ClockEngine : IDisposable
{
    readonly Session session;
    Decoder decoder = new Decoder();
    SerialPort serial;
    BluetoothLink bluetooth;
    readonly byte[] buffer = new byte[512];
    Frame outbound;
    DateTime sendAfter;
    public string Port { get; private set; }
    public bool IsBluetooth { get { return bluetooth!=null; } }
    public bool PortOpen { get { return serial!=null && serial.IsOpen || bluetooth!=null && bluetooth.Open; } }
    public bool Synchronized { get { return PortOpen && session.Synchronized; } }
    bool editingTestValues;
    public bool SavingPair { get { return editingTestValues; } }
    public bool Ready { get { return PortOpen && session.Ready && !editingTestValues; } }
    public bool Busy { get { return session.Busy || editingTestValues; } }
    public string Status { get { return session.Status; } }
    public int? DisplayMode { get { Frame f=Get(6); return f==null ? (int?)null : f.C; } }
    public Color? PreviewColor { get; private set; }
    public DateTime? PreviewEpoch { get; private set; }
    public DateTime EpochReceived { get; private set; }
    DateTime? pendingTime;
    public ClockEngine()
    {
        session=new Session(Send);
        session.Acknowledged+=delegate(Frame f) {
            if(f.Command==32) PreviewColor=Color.FromArgb(f.A,f.B,f.C);
            if(f.Command==23) PreviewColor=null;
            if(f.Command==3 && pendingTime.HasValue)
            {
                PreviewEpoch=pendingTime; EpochReceived=DateTime.UtcNow; pendingTime=null;
            }
        };
    }
    void Send(Frame frame)
    {
        if(outbound!=null) throw new InvalidOperationException("A clock frame is already pending.");
        outbound=frame;
        // The UART needs a settling interval after opening and between its identity
        // reply and the start of the settings stream. Keep the UI thread responsive.
        int settle=frame.Command==0 ? (frame.C==1?400:250) : 65;
        sendAfter=DateTime.UtcNow.AddMilliseconds(settle);
    }
    void Flush()
    {
        if(outbound==null || DateTime.UtcNow<sendAfter) return;
        var bytes=outbound.Bytes();
        outbound=null;
        if(bluetooth!=null) bluetooth.Send(bytes);
        else serial.Write(bytes,0,bytes.Length);
    }
    public Frame Get(int command,int part=0) { return session.Get(command,part); }
    public bool? SwitchState(int key)
    {
        Frame f=Get(key,key==14?3:0);
        if(f==null) return null;
        return (key==4 || key==5 || key==9 || key==12 || key==13 ? f.A : f.C)!=0;
    }
    public void Toggle(int key)
    {
        bool? on=SwitchState(key);
        if(!on.HasValue) throw new InvalidOperationException("Read this setting first.");
        int slot=key==4 || key==5 || key==9 || key==12 || key==13 ? 0 : 2;
        Field(key,slot,on.Value?0:1,key==14?3:0);
    }
    public void Field(int command,int slot,int value,int part=0)
    {
        Frame f=Get(command,part);
        if(f==null) throw new InvalidOperationException("Read this setting first.");
        Apply(new Frame(command,slot==0?value:f.A,slot==1?value:f.B,slot==2?value:f.C));
    }
    public void SetTime(DateTime t)
    {
        if(SwitchState(20)!=false) throw new InvalidOperationException("Turn off tube test mode before setting the real-time clock.");
        var frames=ClockCommands.Time(t);
        pendingTime=t;
        Apply(frames);
    }
    public async Task SavePair(DateTime pair)
    {
        var frames=ClockCommands.Time(pair);
        if(!Ready) throw new InvalidOperationException("Wait for the clock to finish reading.");
        bool restore=SwitchState(20)==false;
        editingTestValues=true;
        Exception failure=null;
        try {
            if(restore) {
                session.Apply(new Frame(20,0,0,1));
                await Finish();
                if(SwitchState(20)!=true) throw new InvalidOperationException("Clock did not confirm test mode.");
            }
            session.Apply(frames);
            await Finish();
        }
        catch(Exception e) { failure=e; }
        try {
                if(restore && PortOpen) {
                    if(!session.Ready) { session.Read(); await Finish(); }
                    if(SwitchState(20)==true) { session.Apply(new Frame(20,0,0,0)); await Finish(); }
                    if(SwitchState(20)!=false) throw new InvalidOperationException("Check the clock’s tube test mode after reconnecting.");
                }
            if(failure!=null) throw new InvalidOperationException("Date and duration may be partially applied. "+failure.Message,failure);
        } finally { editingTestValues=false; }
    }
    async Task Finish()
    {
        while(PortOpen && session.Busy) await Task.Delay(75);
        if(!PortOpen || !session.Ready) throw new InvalidOperationException(Status);
    }
    public void Apply(params Frame[] frames)
    {
        if(!Ready) throw new InvalidOperationException("Connect the clock and finish reading settings.");
        if(SwitchState(20)==true)
            foreach(var f in frames)
                if(f.Command==9 || f.Command==10 || f.Command==21)
                    throw new InvalidOperationException("Turn off tube test mode to edit this setting. Secondary controls are on the Device page.");
        try { session.Apply(frames); }
        catch { Disconnect(); throw; }
    }
    public void Secondary(Frame f)
    {
        if(!Ready || SwitchState(20)!=true)
            throw new InvalidOperationException("Enable tube test mode and wait for its confirmed setting first.");
        try { session.ApplySecondary(f); } catch { Disconnect(); throw; }
    }
    public void SelectPort(string name)
    {
        if(Busy) return;
        Disconnect();
        try {
            serial=new SerialPort(name,115200,Parity.None,8,StopBits.One);
            serial.WriteTimeout=500;
            serial.Open(); Port=name; ReadClock();
        } catch(Exception e) { Failed(e); }
    }
    public void ChooseBluetooth(Form parent)
    {
        if(Busy) return;
        using(var picker=new BluetoothPicker())
        {
            if(picker.ShowDialog(parent)!=DialogResult.OK) return;
            SelectBluetooth(picker.Address);
        }
    }
    public void SelectBluetooth(ulong address)
    {
        if(Busy) return;
        Disconnect();
        try {
            Port="Bluetooth LE";
            bluetooth=new BluetoothLink(address);
            session.Fail("Opening Bluetooth link…");
        } catch(Exception e) { Failed(e); }
    }
    public void ReadClock()
    {
        if(!PortOpen || Busy) return;
        try { session.Read(); } catch(Exception e) { Failed(e); }
    }
    public void Poll()
    {
        try {
            Flush();
            if(bluetooth!=null)
            {
                if(bluetooth.Error!=null) throw new InvalidOperationException(bluetooth.Error);
                if(bluetooth.StartHandshake) ReadClock();
                byte next;
                while(bluetooth.Pop(out next)) decoder.Feed(next,session.Receive);
            }
            if(serial!=null)
            {
                if(!serial.IsOpen) throw new InvalidOperationException("Serial port closed.");
                int bytes=Math.Min(buffer.Length,serial.BytesToRead);
                if(bytes>0)
                {
                    int n=serial.Read(buffer,0,bytes);
                    for(int i=0;i<n;i++) decoder.Feed(buffer[i],session.Receive);
                }
            }
            session.CheckTimeout(DateTime.UtcNow);
        } catch(Exception e) { Failed(e); }
    }
    void Failed(Exception e) { Disconnect(); session.Fail(e.GetBaseException().Message); }
    public void Disconnect()
    {
        if(serial!=null) { try { serial.Dispose(); } finally { serial=null; } }
        if(bluetooth!=null) { bluetooth.Dispose(); bluetooth=null; }
        Port=null; PreviewColor=null; PreviewEpoch=null; pendingTime=null; outbound=null;
        decoder=new Decoder(); session.Fail("Choose the clock’s port to connect.");
    }
    public void Dispose() { Disconnect(); }
}
