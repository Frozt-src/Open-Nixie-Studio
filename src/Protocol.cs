using System;
using System.Collections.Generic;

sealed class Frame
{
    public readonly byte Command, A, B, C;
    public Frame(int command, int a, int b, int c)
    {
        checked { Command=(byte)command; A=(byte)a; B=(byte)b; C=(byte)c; }
    }
    public byte[] Bytes() { return new byte[]{0xea, Command,A,B,C,(byte)(Command^A^B^C),0xfe}; }
    public bool Matches(Frame other)
    { return other!=null && Command==other.Command && A==other.A && B==other.B && C==other.C; }
    internal int Key { get { return Command==14 ? (Command << 8) | A : Command; } }
}

sealed class Decoder
{
    readonly List<byte> buffer = new List<byte>(7);
    public void Feed(byte b, Action<Frame> receive)
    {
        if (buffer.Count==0 && b!=0xea) return;
        buffer.Add(b);
        if (buffer.Count<7) return;
        if (buffer[6]==0xfe && (buffer[1]^buffer[2]^buffer[3]^buffer[4])==buffer[5])
        {
            var frame = new Frame(buffer[1],buffer[2],buffer[3],buffer[4]);
            buffer.Clear();
            receive(frame);
            return;
        }
        int next = buffer.IndexOf(0xea,1);
        buffer.RemoveRange(0,next<0 ? buffer.Count : next);
    }
}

static class ClockCommands
{
    public static Frame[] Time(DateTime t)
    {
        if(t.Year<2000 || t.Year>2099) throw new ArgumentOutOfRangeException("t","Choose a year from 2000 through 2099.");
        return new[]{new Frame(1,t.Year-2000,t.Month,t.Day),new Frame(2,t.Hour,t.Minute,t.Second),
                     new Frame(3,0,0,(int)t.DayOfWeek)};
    }
    public static bool IsSetting(Frame f)
    { return (f.Command>=4 && f.Command<=21 && (f.Command!=14 || (f.A>=1 && f.A<=3))) || f.Command==35; }
}

// Both transport and GUI call this state machine on one thread. Writes are never retried
// automatically because several firmware commands advance a mode on each press.
sealed class Session
{
    enum Phase { Idle, Identify, Settings, Write }
    Phase phase;
    readonly Action<Frame> send;
    readonly Queue<Frame> queue = new Queue<Frame>();
    readonly Dictionary<int,Frame> expected = new Dictionary<int,Frame>();
    Dictionary<int,Frame> values = new Dictionary<int,Frame>();
    readonly Dictionary<int,Frame> incoming = new Dictionary<int,Frame>();
    DateTime deadline;
    public bool Synchronized { get; private set; }
    public bool Busy { get { return phase!=Phase.Idle; } }
    public bool Ready { get { return Synchronized && !Busy; } }
    public string Status { get; private set; }
    public event Action<Frame> Acknowledged;
    public Session(Action<Frame> write) { send=write; Status="Choose the clock’s serial port."; }
    public Frame Get(int cmd,int part=0)
    {
        Frame value;
        return values.TryGetValue(cmd==14 ? (cmd<<8)|part : cmd,out value) ? value : null;
    }
    public void Read()
    {
        if(Busy) return;
        expected.Clear();
        StartRead();
    }
    void StartRead()
    {
        incoming.Clear();
        phase=Phase.Identify;
        Status="Reading clock settings…";
        Transmit(new Frame(0,0,0,1));
    }
    void Transmit(Frame f)
    {
        deadline=DateTime.UtcNow.AddSeconds(8);
        send(f);
    }
    public void Apply(params Frame[] frames)
    { BeginWrite(true,frames); }
    // In test mode the same opcode addresses a separate setting. The ordinary
    // settings dump cannot establish that secondary value.
    public void ApplySecondary(Frame frame)
    { BeginWrite(false,new[]{frame}); }
    void BeginWrite(bool verify,Frame[] frames)
    {
        if (!Ready) throw new InvalidOperationException("Connect and finish reading the clock first.");
        if (frames.Length==0) return;
        expected.Clear();
        foreach (Frame f in frames)
        {
            queue.Enqueue(f);
            if(verify && ClockCommands.IsSetting(f)) expected[f.Key]=f;
        }
        phase=Phase.Write;
        Status="Waiting for clock acknowledgment…";
        Transmit(queue.Peek());
    }
    public void Receive(Frame f)
    {
        if (phase==Phase.Identify)
        {
            if(f.Command!=0 || f.A==255 || (f.A==0 && f.B==0 && f.C<=1)) return;
            phase=Phase.Settings;
            Transmit(new Frame(0,0,0,0));
        }
        else if (phase==Phase.Settings)
        {
            if (f.Command==0 && f.A==255 && f.B==0 && f.C==0)
            {
                // Commands 04..15, three schedule records and power (23).
                if(incoming.Count!=21) { Fail("Incomplete settings. Select Read settings to retry."); return; }
                values=new Dictionary<int,Frame>(incoming);
                Synchronized=true;
                phase=Phase.Idle;
                bool same=true;
                foreach (var pair in expected) if (!pair.Value.Matches(values[pair.Key])) same=false;
                Status=expected.Count==0 ? "Clock settings received." :
                    same ? "Setting confirmed by readback." : "Clock reported a different setting.";
                expected.Clear();
            }
            else if(ClockCommands.IsSetting(f)) { incoming[f.Key]=f; Transmit(f); }
        }
        else if (phase==Phase.Write && queue.Peek().Matches(f))
        {
            queue.Dequeue();
            if(Acknowledged!=null) Acknowledged(f);
            if(queue.Count>0) Transmit(queue.Peek());
            else if(expected.Count>0 || f.Command==22) StartRead();
            else { phase=Phase.Idle; Status="Command acknowledged by the clock."; }
        }
    }
    public void CheckTimeout(DateTime now)
    {
        if(Busy && now>=deadline) {
            string detail=phase==Phase.Identify?"identification":phase==Phase.Settings?
                "settings read ("+incoming.Count+" of 21 records)":"command acknowledgment";
            Fail("Timed out during "+detail+". Read settings before retrying.");
        }
    }
    public void Fail(string reason)
    {
        queue.Clear(); expected.Clear(); incoming.Clear(); values.Clear();
        phase=Phase.Idle; Synchronized=false; Status=reason;
    }
}
