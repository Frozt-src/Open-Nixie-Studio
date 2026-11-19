using System;
using System.Collections.Generic;
using System.Linq;

static class ProtocolTests
{
    static void Check(bool pass, string label)
    {
        if (!pass) throw new Exception(label);
        Console.WriteLine("PASS  " + label);
    }

    // A synthetic settings stream. No workstation or device identifiers are needed.
    static void Snapshot(Session s, int level)
    {
        s.Receive(new Frame(0, 9, 3, 39));
        s.Receive(new Frame(0, 0, 0, 0));
        for (int cmd = 21; cmd >= 4; cmd--)
        {
            if (cmd == 14)
                for (int part = 1; part <= 3; part++) s.Receive(new Frame(14, part, 0, 0));
            else s.Receive(new Frame(cmd, cmd == 9 ? 1 : 0, 0, cmd == 9 ? level : 0));
        }
        s.Receive(new Frame(35, 0, 0, 1));
        s.Receive(new Frame(0, 255, 0, 0));
    }

    public static void Main()
    {
        var f = new Frame(32, 18, 52, 86);
        Check(BitConverter.ToString(f.Bytes()) == "EA-20-12-34-56-50-FE", "RGB wire format");
        var decoded = new List<Frame>();
        var parser = new Decoder();
        byte[] damaged = f.Bytes();
        damaged[5] ^= 1;
        foreach (byte b in damaged.Concat(new byte[]{0, 0xea}).Concat(f.Bytes()).Concat(new Frame(32,234,254,0).Bytes()))
            parser.Feed(b, decoded.Add);
        Check(decoded.Count == 2 && decoded[1].A == 234 && decoded[1].B == 254, "damaged stream resynchronizes and accepts embedded delimiters");
        var sent = new List<Frame>();
        var s = new Session(sent.Add);
        s.Read();
        Check(sent.Count == 1 && sent[0].C == 1 && !s.Ready, "read begins with handshake only");
        Snapshot(s, 7);
        Check(s.Ready && s.Get(9).A == 1 && s.Get(9).C == 7, "complete USB snapshot");
        s.Apply(new Frame(9,1,0,5));
        s.Receive(new Frame(9,1,0,4));
        Check(!s.Ready && s.Get(9).C == 7, "incorrect acknowledgment cannot commit a write");
        s.Receive(new Frame(9,1,0,5));
        Snapshot(s, 5);
        Check(s.Ready && s.Get(9).C == 5 && s.Status.Contains("confirmed"), "write confirmed by independent readback");
        s.Apply(new Frame(9,1,0,6));
        s.Receive(new Frame(9,1,0,6));
        Snapshot(s, 5);
        Check(s.Status.Contains("different"), "mismatched readback reported");
        s.Read();
        s.Receive(new Frame(0,9,3,39));
        s.Receive(new Frame(0,255,0,0));
        Check(!s.Ready && s.Get(9) == null, "incomplete snapshot invalidates old state");
        s.Read();
        s.CheckTimeout(DateTime.UtcNow.AddMinutes(3));
        Check(!s.Busy && !s.Ready, "timeout releases operation for retry");
        var time = ClockCommands.Time(new DateTime(2024,2,29,23,58,59));
        Check(time[0].A==24 && time[1].C==59 && time[2].C==4, "calendar and weekday encoding");
        s.Read();
        Snapshot(s,7);
        s.Apply(time);
        int writes=sent.Count;
        s.Receive(time[2]);
        Check(sent.Count==writes && s.Busy,"out-of-order time ACK cannot advance the queue");
        foreach(var part in time) s.Receive(part);
        Check(s.Ready && sent.Count==writes+2,"calendar, time and weekday acknowledged in order");
        s.ApplySecondary(new Frame(9,1,0,4));
        s.Receive(new Frame(9,1,0,4));
        Check(s.Ready && s.Get(9).C==7,"test-mode sensitivity keeps ordinary brightness intact");
        s.Apply(new Frame(23,0,0,2));
        int count=sent.Count;
        s.CheckTimeout(DateTime.UtcNow.AddMinutes(1));
        Check(sent.Count==count && !s.Ready,"timed-out effect command is never repeated automatically");
    }
}
