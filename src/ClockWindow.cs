using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using(var engine=new ClockEngine())
        using(var window=new ClockWindow(engine)) Application.Run(window);
    }
}

sealed class ClockWindow : Form
{
    readonly ClockEngine clock;
    readonly Panel host=new Panel { BackColor=Theme.Back, AutoScroll=true };
    readonly List<Action> bindings=new List<Action>();
    readonly Timer timer=new Timer { Interval=50 };
    readonly StudioSurface surface;
    readonly Font heading=new Font("Segoe UI Semibold",14);
    string page="Overview";
    TableLayoutPanel grid;
    int ticks;
    public ClockWindow(ClockEngine engine)
    {
        clock=engine;
        Text="XGClock Studio"; Font=new Font("Segoe UI",10); FormBorderStyle=FormBorderStyle.None;
        MinimumSize=new Size(860,570); StartPosition=FormStartPosition.CenterScreen;
        Rectangle work=Screen.FromPoint(Cursor.Position).WorkingArea;
        double scale=Math.Min(1,Math.Min((work.Width-40)/1497.0,(work.Height-40)/966.0));
        ClientSize=new Size((int)(1497*scale),(int)(966*scale));
        surface=new StudioSurface(this,clock,host,ShowPage);
        Controls.Add(surface);
        timer.Tick+=delegate {
            clock.Poll();
            if(++ticks%4==0) { foreach(Action bind in bindings) bind(); surface.UpdateView(page,null); }
        };
        timer.Start();
        FormClosing+=delegate(object sender,FormClosingEventArgs args) {
            if(clock.SavingPair && args.CloseReason==CloseReason.UserClosing) {
                args.Cancel=true;
                MessageBox.Show(this,"The date and duration are being saved. Wait for the clock to finish.","XGClock Studio");
            }
        };
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing) { timer.Dispose(); heading.Dispose(); }
        base.Dispose(disposing);
    }
    internal void ShowPage(string next)
    {
        page=next; bindings.Clear(); host.SuspendLayout();
        if(grid!=null) grid.Dispose();
        grid=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,BackColor=Theme.Back };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        host.Controls.Add(grid);
        switch(page)
        {
            case "Display":
                Group("Tube display", Choice("Display mode",6,"Time","Date","Timer","Anniversary"),
                    Choice("Digit transition",7,"Normal","Crossfade","Fade","Roll"),
                    Choice("Separator",8,"Blink","Breathe","Steady","Off"), Toggle("12-hour format",19));
                Group("Brightness", Number("Manual brightness",9,2,1,10),Toggle("Automatic brightness",9),Toggle("Night mode",17));
                Group("RGB backlight", ActionRow("Solid color","Choose color",PickColor),
                    ActionRow("Color","Next color",delegate { clock.Apply(new Frame(23,0,0,1)); }),
                    ActionRow("Animation","Next effect",delegate { clock.Apply(new Frame(23,0,0,2)); }),
                    ActionRow("Backlight power","Toggle",delegate { clock.Apply(new Frame(23,0,0,3)); }),
                    Toggle("Color by weekday",18));
                Group("Custom display",ActionRow("Six digits","Edit",CustomDigits,delegate {
                    Frame f=clock.Get(10); return f==null?"Edit":string.Format("{0:D2} {1:D2} {2:D2}",f.A,f.B,f.C);
                }),Toggle("Custom display enabled",11));
                break;
            case "Sound":
                Group("Sound & chimes",Number("Volume",13,2,0,10),Number("Ringtone",13,1,1,6),
                      Toggle("Button tones",13),Toggle("Hourly chime",16),Toggle("English voice",15));
                break;
            case "Alarms & schedules":
                Group("Alarms",Hour("Alarm 1",4),Toggle("Alarm 1 enabled",4),Hour("Alarm 2",5),Toggle("Alarm 2 enabled",5));
                Group("Power schedule",Hour("Power on",14,1),Hour("Power off",14,2),Toggle("Use schedule",14));
                Group("Motion auto-off",ActionRow("Idle timeout","Edit seconds",Idle,delegate {
                    Frame f=clock.Get(12); return f==null?"Edit seconds":(f.B*60+f.C)+" seconds";
                }),Toggle("Use motion sensor",12));
                Group("Anniversary & countdown",ActionRow("Saved date and duration","Edit pair",TestPair),
                      ActionRow("Timer direction","Reselect timer",delegate {
                          if(clock.SwitchState(20)!=true) throw new InvalidOperationException("Enable tube test mode before changing timer direction.");
                          clock.Apply(new Frame(6,0,0,2));
                      }),Toggle("Tube test mode",20));
                break;
            case "Device":
                Group("Clock maintenance",Number("Protection interval (min)",21,2,0,30),Toggle("Tube test mode",20),
                    Toggle("Clock power",35),ActionRow("Factory settings","Reset…",delegate {
                        if(MessageBox.Show(this,"Restore factory settings on the connected clock? Saved settings may be lost.",
                            "Reset clock",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.OK)
                            clock.Apply(new Frame(22,0,0,1));
                    }));
                Group("Tube test mode controls",
                    ActionRow("Motion sensitivity","Set level…",delegate { SecondaryNumber(true); }),
                    ActionRow("Alarm duration","Set minutes…",delegate { SecondaryNumber(false); }),
                    ActionRow("Visible tubes","Choose tubes…",TubeMask));
                break;
        }
        host.ResumeLayout(true);
        foreach(Action bind in bindings) bind();
    }
    void Group(string name,params Control[] rows)
    {
        var body=new TableLayoutPanel { AutoSize=true,Dock=DockStyle.Top,ColumnCount=1,Padding=new Padding(14),Margin=new Padding(5),BackColor=Theme.Surface };
        body.Controls.Add(new Label { Text=name,Font=heading,AutoSize=true,Margin=new Padding(2,2,2,12),ForeColor=Theme.Text });
        foreach(var row in rows) body.Controls.Add(row);
        grid.Controls.Add(body);
    }
    Control ActionRow(string label,string caption,Action action,Func<string> current=null)
    {
        var row=new TableLayoutPanel { Dock=DockStyle.Top,Height=46,ColumnCount=2,Margin=new Padding(0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));
        row.Controls.Add(new Label { Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Theme.Muted });
        var b=new Pill(caption) { Dock=DockStyle.Fill,Margin=new Padding(0,3,0,3),AccessibleDescription=label,Emphasis=false };
        row.Controls.Add(b);
        b.Click+=delegate {
            try { if(clock.Ready) action(); }
            catch(Exception e) { MessageBox.Show(this,e.GetBaseException().Message,label,MessageBoxButtons.OK,MessageBoxIcon.Information); }
        };
        bindings.Add(delegate {
            b.Enabled=clock.Ready;
            if(current!=null) b.Text=current();
            bool selected=b.Text=="On";
            if(b.Selected!=selected) { b.Selected=selected; b.Invalidate(); }
            b.AccessibleName=label+" "+b.Text;
        });
        return row;
    }
    Control Toggle(string label,int command)
    {
        return ActionRow(label,"Unknown",delegate { clock.Toggle(command); },delegate {
            bool? on=clock.SwitchState(command); return on.HasValue ? (on.Value?"On":"Off") : "Unknown";
        });
    }
    Control Choice(string label,int command,params string[] choices)
    {
        return ActionRow(label,"Choose",delegate {
            Frame f=clock.Get(command);
            var box=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList };
            box.Items.AddRange(choices); box.SelectedIndex=f.C<choices.Length ? f.C : 0;
            if(Edit(label,box)) clock.Field(command,2,box.SelectedIndex);
        },delegate { Frame f=clock.Get(command); return f!=null && f.C<choices.Length?choices[f.C]:"Choose"; });
    }
    Control Number(string label,int command,int slot,int min,int max)
    {
        Func<int> value=delegate { var f=clock.Get(command); return f==null?min:slot==1?f.B:f.C; };
        return ActionRow(label,"Set",delegate {
            var n=Numeric(min,max,value());
            if(Edit(label,n)) clock.Field(command,slot,(int)n.Value);
        },delegate { return clock.Get(command)==null?"Set":value().ToString(); });
    }
    Control Hour(string label,int cmd,int part=0)
    {
        return ActionRow(label,"Edit",delegate {
            var f=clock.Get(cmd,part);
            var t=Picker("HH:mm",DateTime.Today.AddHours(f.B).AddMinutes(f.C));
            if(Edit(label,t)) clock.Apply(new Frame(cmd,f.A,t.Value.Hour,t.Value.Minute));
        },delegate { var f=clock.Get(cmd,part); return f==null?"Edit":string.Format("{0:D2}:{1:D2}",f.B,f.C); });
    }
    static NumericUpDown Numeric(int min,int max,int current)
    { return new NumericUpDown { Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,current)) }; }
    static DateTimePicker Picker(string format,DateTime value)
    { return new DateTimePicker { Format=DateTimePickerFormat.Custom,CustomFormat=format,ShowUpDown=true,Value=value }; }
    bool Edit(string title,params Control[] controls)
    {
        using(var form=new Form { Text=title,StartPosition=FormStartPosition.CenterParent,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,
            MaximizeBox=false,MinimizeBox=false,ClientSize=new Size(390,68+controls.Length*38) })
        {
            var panel=new FlowLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(14),FlowDirection=FlowDirection.TopDown,WrapContents=false };
            foreach(var c in controls) { c.Width=345; panel.Controls.Add(c); }
            var actions=new FlowLayoutPanel { AutoSize=true };
            var ok=new Button { Text="Apply",DialogResult=DialogResult.OK };
            var cancel=new Button { Text="Cancel",DialogResult=DialogResult.Cancel };
            actions.Controls.Add(ok); actions.Controls.Add(cancel); panel.Controls.Add(actions); form.Controls.Add(panel);
            form.AcceptButton=ok; form.CancelButton=cancel;
            return form.ShowDialog(this)==DialogResult.OK;
        }
    }
    void Idle()
    {
        Frame f=clock.Get(12);
        var n=Numeric(0,6000,f.B*60+f.C);
        if(Edit("Idle timeout in seconds",n)) clock.Apply(new Frame(12,f.A,(int)n.Value/60,(int)n.Value%60));
    }
    void SecondaryNumber(bool sensitivity)
    {
        if(clock.SwitchState(20)!=true) throw new InvalidOperationException("Enable tube test mode first.");
        var n=Numeric(1,sensitivity?10:30,1);
        if(Edit(sensitivity?"New motion sensitivity (1–10)":"New alarm duration in minutes",n))
            clock.Secondary(sensitivity?new Frame(9,clock.Get(9).A,0,(int)n.Value):new Frame(21,0,0,(int)n.Value));
    }
    void TubeMask()
    {
        if(clock.SwitchState(20)!=true) throw new InvalidOperationException("Enable tube test mode first.");
        var mask=new TextBox { MaxLength=6 };
        if(!Edit("New tube mask · six digits, 1 visible, 0 hidden",mask)) return;
        if(!System.Text.RegularExpressions.Regex.IsMatch(mask.Text,"^[01]{6}$"))
            throw new InvalidOperationException("Enter six zeroes or ones, from left to right.");
        clock.Secondary(new Frame(10,int.Parse(mask.Text.Substring(0,2)),int.Parse(mask.Text.Substring(2,2)),int.Parse(mask.Text.Substring(4,2))));
    }
    void CustomDigits()
    {
        Frame f=clock.Get(10);
        var a=Numeric(0,99,f.A); var b=Numeric(0,99,f.B); var c=Numeric(0,99,f.C);
        if(Edit("Digit pairs · left, center, right",a,b,c)) clock.Apply(new Frame(10,(int)a.Value,(int)b.Value,(int)c.Value));
    }
    void PickColor()
    {
        using(var picker=new ColorDialog { FullOpen=true,Color=clock.PreviewColor??Color.OrangeRed })
            if(picker.ShowDialog(this)==DialogResult.OK) clock.Apply(new Frame(32,picker.Color.R,picker.Color.G,picker.Color.B));
    }
    async void TestPair()
    {
        var date=Picker("yyyy-MM-dd",DateTime.Today); date.ShowCheckBox=true; date.Checked=false;
        date.MinDate=new DateTime(2000,1,1); date.MaxDate=new DateTime(2099,12,31);
        var duration=new TextBox();
        if(!Edit("Replace both saved values",new Label { Text="Select the new anniversary date" },date,new Label { Text="Enter the new countdown as HH:mm:ss" },duration)) return;
        try {
            TimeSpan span;
            if(!date.Checked || !TimeSpan.TryParseExact(duration.Text.Trim(),@"hh\:mm\:ss",System.Globalization.CultureInfo.InvariantCulture,out span))
                throw new InvalidOperationException("Select a date and enter a duration from 00:00:00 through 23:59:59.");
            await clock.SavePair(date.Value.Date+span);
        } catch(Exception e) {
            if(!IsDisposed) MessageBox.Show(this,e.GetBaseException().Message,"Anniversary & countdown");
        }
    }
}
