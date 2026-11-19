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
    readonly SettingsViewport host=new SettingsViewport();
    readonly List<Action> bindings=new List<Action>();
    readonly Timer timer=new Timer { Interval=50 };
    readonly StudioSurface surface;
    readonly Font heading=new Font("Segoe UI Semibold",14);
    string page="Overview";
    Panel grid;
    bool arranging;
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
        host.SizeChanged+=delegate { ArrangeCards(); };
        UpdateWindowShape();
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
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateWindowShape();
    }
    void UpdateWindowShape()
    {
        if(ClientSize.Width<10||ClientSize.Height<10)return;
        var old=Region;
        if(WindowState==FormWindowState.Maximized) Region=null;
        else using(var path=Theme.Round(new RectangleF(0,0,ClientSize.Width,ClientSize.Height),
                        18*Math.Min(ClientSize.Width/1497f,ClientSize.Height/966f))) Region=new Region(path);
        if(old!=null)old.Dispose();
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
        grid=new Panel { Location=Point.Empty,BackColor=Theme.Back,Margin=Padding.Empty };
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
                Group("Clock sound",Number("Volume",13,2,0,10),Number("Ringtone",13,1,1,6),Toggle("Button tones",13));
                Group("Voice & chimes",Toggle("Hourly chime",16),Toggle("English voice",15));
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
        ArrangeCards();
        foreach(Action bind in bindings) bind();
    }
    static readonly Dictionary<string,string> Glyphs=new Dictionary<string,string> {
            {"Tube display","\uE7F4"},{"Brightness","\uE706"},{"RGB backlight","\uE790"},{"Custom display","\uECA5"},
            {"Clock sound","\uE767"},{"Voice & chimes","\uEA8F"},{"Alarms","\uE916"},{"Power schedule","\uE787"},
            {"Motion auto-off","\uE81D"},{"Anniversary & countdown","\uE823"},{"Clock maintenance","\uE90F"},{"Tube test mode controls","\uE9D9"}
        };
    void Group(string name,params Control[] rows)
    {
        var body=new SettingsCard { Dock=DockStyle.None,RowCount=rows.Length+2 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute,49));
        body.Controls.Add(new GroupTitle(name,Glyphs[name],heading),0,0);
        foreach(var row in rows) {
            int index=body.RowStyles.Count;
            body.RowStyles.Add(new RowStyle(SizeType.Absolute,47));
            body.Controls.Add(row,0,index);
        }
        body.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        body.Controls.Add(new Panel { Margin=Padding.Empty,Dock=DockStyle.Fill,BackColor=StudioStyle.Paper },0,rows.Length+1);
        body.Tag=83+rows.Length*47;
        grid.Controls.Add(body);
    }
    void ArrangeCards()
    {
        if(arranging||grid==null||grid.IsDisposed||host.ClientSize.Width<60)return;
        arranging=true;
        try {
            const int gap=16;
            int available=host.ClientSize.Width;
            int columns=available>=760?2:1;
            int height=0;
            for(int i=0;i<grid.Controls.Count;i+=columns) {
                int h=(int)grid.Controls[i].Tag;
                if(columns==2 && i+1<grid.Controls.Count)h=Math.Max(h,(int)grid.Controls[i+1].Tag);
                height+=h+gap;
            }
            height=Math.Max(0,height-gap);
            int width=available-(height>host.ClientSize.Height?16:0);
            int col=(width-gap*(columns-1))/columns;
            int y=0;
            for(int i=0;i<grid.Controls.Count;i+=columns) {
                int h=(int)grid.Controls[i].Tag;
                if(columns==2 && i+1<grid.Controls.Count)h=Math.Max(h,(int)grid.Controls[i+1].Tag);
                for(int j=0;j<columns&&i+j<grid.Controls.Count;j++)
                    grid.Controls[i+j].Bounds=new Rectangle(j*(col+gap),y,j==0?col:width-col-gap,h);
                y+=h+gap;
            }
            host.Fit(grid,width,height);
        } finally { arranging=false; }
    }
    Control ActionRow(string label,string caption,Action action,Func<string> current=null,bool toggle=false)
    {
        var row=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty,BackColor=StudioStyle.Paper };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,toggle?62:150));
        row.Paint+=delegate(object sender,PaintEventArgs e) {
            using(var pen=new Pen(Color.FromArgb(229,234,231))) e.Graphics.DrawLine(pen,0,0,row.Width,0);
        };
        row.Controls.Add(new Label { Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Theme.Text,Margin=new Padding(0,1,0,0),Font=StudioStyle.RowFont,UseMnemonic=false });
        var b=new Pill(caption) { Dock=DockStyle.Fill,Margin=new Padding(0,6,0,6),AccessibleDescription=label,IsSwitch=toggle };
        row.Controls.Add(b);
        b.Enter+=delegate { host.Reveal(b); };
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
        },true);
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
            MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false,BackColor=StudioStyle.Paper,ClientSize=new Size(422,90+controls.Length*39) })
        {
            var panel=new FlowLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(22,18,22,14),FlowDirection=FlowDirection.TopDown,WrapContents=false };
            foreach(var c in controls) { c.Width=372; c.Margin=new Padding(0,2,0,8); panel.Controls.Add(c); }
            var actions=new FlowLayoutPanel { Width=372,Height=40,Margin=new Padding(0,6,0,0),FlowDirection=FlowDirection.RightToLeft };
            var ok=new Pill("Apply") { Width=90,Height=34,Emphasis=true,DialogResult=DialogResult.OK };
            var cancel=new Pill("Cancel") { Width=90,Height=34,DialogResult=DialogResult.Cancel };
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
