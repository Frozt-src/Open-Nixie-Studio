using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Ports;

using System.Windows.Forms;

static class Theme
{
    public static readonly Color Back = Color.FromArgb(247, 249, 248);
    public static readonly Color Rail = Color.FromArgb(231, 238, 235);
    public static readonly Color Surface = Color.FromArgb(251, 252, 251);
    public static readonly Color Raised = Color.FromArgb(230, 237, 234);
    public static readonly Color Outline = Color.FromArgb(200, 211, 207);
    public static readonly Color Text = Color.FromArgb(24, 36, 35);
    public static readonly Color Muted = Color.FromArgb(78, 91, 88);
    public static readonly Color Accent = Color.FromArgb(22, 111, 101);
    public static readonly Color Ink = Color.FromArgb(255, 255, 255);
    public static readonly Color Amber = Color.FromArgb(255, 181, 108);
    public static readonly Color Green = Color.FromArgb(168, 227, 195);
    public static readonly Color Error = Color.FromArgb(255, 171, 164);

    public static GraphicsPath Round(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = 2 * Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

sealed class Pill : Button
{
    public bool Emphasis;
    public bool Selected;
    bool hovered;

    public Pill(string text)
    {
        Text = text;
        AccessibleName = text;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI Semibold", 10f);
        Size = new Size(130, 38);
        Margin = new Padding(0);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent.BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color fill = !Enabled ? Theme.Surface : Emphasis ? Theme.Accent
            : Selected ? Color.FromArgb(185, 223, 214) : hovered ? Color.FromArgb(210, 228, 221) : Theme.Raised;
        Color ink = !Enabled ? Color.FromArgb(128, 123, 137) : Emphasis ? Theme.Ink : Theme.Text;
        using (var outline = Theme.Round(new RectangleF(1, 1, Width - 3, Height - 3), Selected ? 15 : 18))
        using (var brush = new SolidBrush(fill))
        using (var border = new Pen(Focused ? Theme.Accent : Theme.Outline, Focused ? 2 : 1))
        {
            g.FillPath(brush, outline);
            if (Focused || (!Emphasis && !Selected)) g.DrawPath(border, outline);
        }
        int inset = Width < 82 ? 3 : 10;
        TextRenderer.DrawText(g, Text, Font, new Rectangle(inset, 0, Width - 2 * inset, Height), ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

sealed class Card : Panel
{
    public Card()
    {
        BackColor = Theme.Surface;
        Margin = new Padding(6);
        Padding = new Padding(20, 12, 20, 12);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width < 2 || Height < 2) return;
        using (var path = Theme.Round(new RectangleF(0, 0, Width, Height), 22))
        {
            Region old = Region;
            Region = new Region(path);
            if (old != null) old.Dispose();
        }
    }
}

sealed class StudioSurface : Panel
{
    sealed class Hit
    {
        public string Name;
        public Rectangle Bounds;
        public bool Overview;
        public Action Action;
    }
    readonly ClockWindow window;
    readonly ClockEngine clock;
    readonly Panel settings;
    readonly Action<string> navigate;
    readonly Bitmap skin, faces;
    readonly List<Hit> hits = new List<Hit>();
    readonly ToolTip tips = new ToolTip();
    readonly Font regular = new Font("Segoe UI", 18, GraphicsUnit.Pixel);
    readonly Font bold = new Font("Segoe UI Semibold", 20, GraphicsUnit.Pixel);
    readonly Font headline = new Font("Segoe UI Semibold", 41, GraphicsUnit.Pixel);
    static readonly Color white = Color.FromArgb(248,249,248), ink = Color.FromArgb(24,32,33),
        teal = Color.FromArgb(21,112,103), muted = Color.FromArgb(78,88,90), dark = Color.FromArgb(24,37,36);
    static readonly string[] pages = {"Overview","Display","Sound","Alarms & schedules","Device"};
    readonly int[] centers = {581,702,861,981,1145,1263};
    string page = "Overview", hovered = "", notice = "";
    DateTime noticeUntil, nextRead = DateTime.UtcNow.AddSeconds(90), edit = DateTime.Now;
    int percent = 72, scene = 0, focusIndex = -1;
    bool seconds = true, hours24 = true, dragging, editing, keyFocus;
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    ToolStripDropDown picker;
    bool overview { get { return page == "Overview"; } }

    public StudioSurface(ClockWindow owner, ClockEngine engine, Panel settingsPanel, Action<string> show)
    {
        window=owner; clock=engine; settings=settingsPanel; navigate=show;
        Dock=DockStyle.Fill; BackColor=white; TabStop=true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable,true);
        skin=Artwork("Host.png"); faces=Artwork("Cathodes.png");
        Controls.Add(settings); settings.Dock=DockStyle.None; settings.Visible=false;
        Add("Close",60,18,22,32,false,window.Close);
        Add("Minimize",86,18,22,32,false,delegate {window.WindowState=FormWindowState.Minimized;});
        Add("Maximize or restore",112,18,22,32,false,Maximize);
        Add("USB connection",1347,15,52,39,false,Ports);
        Add("Settings",1415,15,50,39,false,delegate{Go("Device");});
        Add("More options",1478,15,52,39,false,More);
        for(int i=0;i<pages.Length;i++){string target=pages[i];Add(target,60,215+i*60,243,57,false,delegate{Go(target);});}
        Add("Clock connection details",63,883,238,71,false,Ports);
        Add("Sync from Computer",1245,100,272,49,true,delegate{Write(delegate{clock.SetTime(DateTime.Now);});});
        Add("Brightness",464,540,355,43,true,delegate{});
        Add("24-hour",920,540,155,43,true,delegate{
            if(clock.Synchronized) Write(delegate{clock.Toggle(0x13);});
            else hours24=!hours24;
        });
        Add("Seconds",1100,540,159,43,true,delegate{seconds=!seconds; Say("Preview seconds " + (seconds?"on":"off"));});
        Add("Display settings",1317,539,193,46,true,delegate{Go("Display");});
        Add("Serial port",492,693,207,41,true,Ports);
        Add("Connect or refresh",361,755,338,48,true,Read);
        Add("Date",814,690,282,40,true,delegate{Pick(true);});
        Add("Time",814,736,282,40,true,delegate{Pick(false);});
        Add("Apply Time",763,788,333,44,true,delegate{DateTime selected=edit;Write(delegate{clock.SetTime(selected);});});
        for(int i=0;i<3;i++){int id=i;Add(new[]{"Daylight","Nightfall","Focus"}[i],1155+i*117,722,117,78,true,delegate{SetBrightness(new[]{72,30,60}[id]);});}
        Resize+=delegate{LayoutSettings();};
        MouseDoubleClick+=delegate(object s,MouseEventArgs e){Point p=Board(e.Location);if(p.Y<57&&p.X>160&&p.X<1340) Maximize();};
        menu.Closed+=delegate{editing=false;};
    }
    static Bitmap Artwork(string name)
    {
        using(var stream=typeof(StudioSurface).Assembly.GetManifestResourceStream(name))
        using(var original=new Bitmap(stream)) return new Bitmap(original);
    }
    void Add(string name,int x,int y,int w,int h,bool home,Action action)
    { hits.Add(new Hit{Name=name,Bounds=new Rectangle(x,y,w,h),Overview=home,Action=action}); }
    void Go(string target){navigate(target);page=target;LayoutSettings();Invalidate();}
    void Maximize(){window.WindowState=window.WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
    void LayoutSettings()
    {
        settings.Visible=!overview;
        settings.Bounds=new Rectangle(Pixel(330,180),new Size((int)(1195*Width/1497f),(int)(719*Height/966f)));
    }
    Point Pixel(int x,int y){return new Point((int)((x-45)*Width/1497f),(int)((y-12)*Height/966f));}
    Point Board(Point p){return new Point(45+(int)(p.X*1497f/Math.Max(1,Width)),12+(int)(p.Y*966f/Math.Max(1,Height)));}
    Hit At(Point p){return hits.Find(delegate(Hit h){return (!h.Overview||overview)&&h.Bounds.Contains(p);});}
    void Say(string message){notice=message;noticeUntil=DateTime.UtcNow.AddSeconds(6);Invalidate();}
    void Write(Action fn)
    {
        if(!clock.Ready){Say("Connect the clock and wait for its settings.");return;}
        fn();nextRead=DateTime.UtcNow.AddSeconds(90);
    }
    void Read(){if(clock.PortOpen){if(!clock.Busy)clock.ReadClock();}else Ports();nextRead=DateTime.UtcNow.AddSeconds(90);}
    void SetBrightness(int value)
    {
        int desired=Math.Max(0,Math.Min(100,value));
        if(clock.Synchronized)
        {
            if(!clock.Ready){Say("Wait for the clock to finish reading.");return;}
            int level=Math.Max(1,Math.Min(10,(int)Math.Round(desired/10.0)));
            clock.Field(9,2,level); desired=level*10;
        }
        percent=desired; scene=(percent==70||percent==72)?0:percent==30?1:percent==60?2:-1;
    }
    void Ports()
    {
        menu.Items.Clear();
        string[] ports=SerialPort.GetPortNames();
        foreach(string p in ports){
            var item=menu.Items.Add(p+(p==clock.Port?"   ✓":""));
            string selected=p;
            item.Click+=delegate{if(selected==clock.Port && clock.PortOpen)Read();else clock.SelectPort(selected);};
        }
        if(ports.Length==0)menu.Items.Add("No serial ports found").Enabled=false;
        menu.Items.Add("Find a Bluetooth clock…",null,delegate{clock.ChooseBluetooth(window);});
        if(clock.PortOpen)menu.Items.Add("Disconnect",null,delegate{clock.Disconnect();}).Enabled=!clock.Busy;
        ShowMenu();
    }
    void More()
    {
        menu.Items.Clear();
        menu.Items.Add("Read settings from clock",null,delegate{Read();});
        menu.Items.Add("Display settings",null,delegate{Go("Display");});
        menu.Items.Add("Device settings",null,delegate{Go("Device");});
        menu.Items.Add("Maximize or restore",null,delegate{Maximize();});
        ShowMenu();
    }
    void ContextFor(Hit hit,Point board)
    {
        if(hit!=null&&(hit.Name=="Serial port"||hit.Name=="USB connection")){Ports();return;}
        if(overview && board.Y>=190 && board.Y<=600 && board.X>=337)
        {
            menu.Items.Clear();
            menu.Items.Add(seconds?"Hide seconds in preview":"Show seconds in preview",null,delegate{seconds=!seconds;Invalidate();});
            menu.Items.Add("Read settings from clock",null,delegate{Read();});
            menu.Items.Add("Display settings",null,delegate{Go("Display");});
            ShowMenu();
        }else More();
    }
    void ShowMenu(){editing=true; Point where=PointToClient(Cursor.Position);if(!ClientRectangle.Contains(where))where=Pixel(840,530);menu.Show(this,where);}
    void Pick(bool date)
    {
        if(picker!=null)picker.Dispose();
        picker=new ToolStripDropDown();editing=true;
        if(date){
            var day=new MonthCalendar{MinDate=new DateTime(2000,1,1),MaxDate=new DateTime(2099,12,31),MaxSelectionCount=1,SelectionStart=edit.Date,SelectionEnd=edit.Date};
            day.DateSelected+=delegate(object s,DateRangeEventArgs e){edit=e.Start.Date+edit.TimeOfDay;picker.Close();};
            picker.Items.Add(new ToolStripControlHost(day));
        }else{
            var panel=new FlowLayoutPanel{Size=new Size(222,40),BackColor=white};
            var time=new DateTimePicker{Format=DateTimePickerFormat.Custom,CustomFormat="HH:mm:ss",ShowUpDown=true,Value=edit,Width=122};
            var ok=new Button{Text="Done",Width=72};
            ok.Click+=delegate{edit=edit.Date+time.Value.TimeOfDay;picker.Close();};panel.Controls.Add(time);panel.Controls.Add(ok);
            picker.Items.Add(new ToolStripControlHost(panel));
        }
        picker.Closed+=delegate{editing=false;Invalidate();};picker.Show(this,Pixel(815,date?731:775));
    }
    public void UpdateView(string active,string error)
    {
        if(page!=active){page=active;LayoutSettings();}
        if(clock.Synchronized){
            bool? shortHours=clock.SwitchState(0x13);
            if(shortHours.HasValue)hours24=!shortHours.Value;
            if(!dragging && clock.Ready){
                int max=10;
                if(max>0){percent=Math.Max(0,Math.Min(100,100*clock.Get(9).C/max));scene=(percent==70||percent==72)?0:percent==30?1:percent==60?2:-1;}
            }
            if(!dragging && !editing && Form.ActiveForm==window && clock.Ready && DateTime.UtcNow>=nextRead)Read();
        }
        Invalidate();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);Point p=Board(e.Location);Hit h=At(p);Focus();keyFocus=false;
        if(e.Button==MouseButtons.Right){ContextFor(h,p);return;}
        if(e.Button!=MouseButtons.Left)return;
        if(h!=null){focusIndex=hits.IndexOf(h);if(h.Name=="Brightness"){dragging=true;Capture=true;Drag(p);}return;}
        if(p.Y<57){ReleaseCapture();SendMessage(window.Handle,0xA1,(IntPtr)2,IntPtr.Zero);}
        else if(p.X>1523&&p.Y>953){ReleaseCapture();SendMessage(window.Handle,0xA1,(IntPtr)17,IntPtr.Zero);}
    }
    void Drag(Point p){percent=Math.Max(0,Math.Min(100,(p.X-478)*100/314));Invalidate();}
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);Point p=Board(e.Location);if(dragging){Drag(p);return;}
        Hit h=At(p);string name=h==null?"":h.Name;
        if(name!=hovered){hovered=name;tips.SetToolTip(this,name);Cursor=h==null?Cursors.Default:Cursors.Hand;Invalidate();}
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);if(e.Button!=MouseButtons.Left)return;
        try{
            if(dragging){dragging=false;Capture=false;SetBrightness(percent);}
            else{Hit h=At(Board(e.Location));if(h!=null&&focusIndex==hits.IndexOf(h))h.Action();}
        }catch(Exception ex){Say(ex.GetBaseException().Message);}Invalidate();
    }
    protected override bool IsInputKey(Keys key){return (key&Keys.KeyCode)==Keys.Left||(key&Keys.KeyCode)==Keys.Right||base.IsInputKey(key);}
    protected override AccessibleObject CreateAccessibilityInstance(){return new SurfaceAccess(this);}
    sealed class SurfaceAccess : ControlAccessibleObject
    {
        readonly StudioSurface s;
        public SurfaceAccess(StudioSurface surface):base(surface){s=surface;}
        List<Hit> Targets(){return s.hits.FindAll(h=>!h.Overview||s.overview);}
        public override int GetChildCount(){return Targets().Count+Math.Max(0,base.GetChildCount());}
        public override AccessibleObject GetChild(int index){
            var a=Targets();if(index<0)return null;
            return index<a.Count?new TargetAccess(s,a[index]):base.GetChild(index-a.Count);
        }
    }
    sealed class TargetAccess : AccessibleObject
    {
        readonly StudioSurface s;readonly Hit hit;
        public TargetAccess(StudioSurface surface,Hit target){s=surface;hit=target;}
        public override string Name{get{return hit.Name;}set{}}
        public override string DefaultAction{get{return "Activate";}}
        public override AccessibleRole Role{get{return hit.Name=="Brightness"?AccessibleRole.Slider:AccessibleRole.PushButton;}}
        public override Rectangle Bounds{get{
            Rectangle r=hit.Bounds;Point a=s.Pixel(r.Left,r.Top),b=s.Pixel(r.Right,r.Bottom);
            return s.RectangleToScreen(Rectangle.FromLTRB(a.X,a.Y,b.X,b.Y));
        }}
        public override void DoDefaultAction(){s.BeginInvoke(new Action(delegate{
            try{hit.Action();}catch(Exception e){s.Say(e.GetBaseException().Message);}
        }));}
        public override string Value{get{return hit.Name=="Brightness"?s.percent+"%":null;}set{
            int n;if(hit.Name=="Brightness" && int.TryParse((value??"").TrimEnd('%'),out n))
                s.BeginInvoke(new Action(delegate{s.SetBrightness(n);}));
        }}
    }
    protected override bool ProcessCmdKey(ref Message m,Keys key)
    {
        if(!overview && settings.ContainsFocus)return base.ProcessCmdKey(ref m,key);
        if(key==Keys.Tab||key==(Keys.Shift|Keys.Tab)){
            int direction=key==Keys.Tab?1:-1;
            do{focusIndex=(focusIndex+direction+hits.Count)%hits.Count;}while(hits[focusIndex].Overview&&!overview);
            keyFocus=true;Invalidate();return true;
        }
        if(keyFocus&&focusIndex>=0){
            Hit h=hits[focusIndex];
            try{
                if(key==Keys.Enter||key==Keys.Space){h.Action();Invalidate();return true;}
                if(h.Name=="Brightness"&&(key==Keys.Left||key==Keys.Right)){SetBrightness(percent+(key==Keys.Left?-10:10));return true;}
            }catch(Exception ex){Say(ex.GetBaseException().Message);return true;}
        }
        if(key==Keys.Apps){More();return true;}
        return base.ProcessCmdKey(ref m,key);
    }
    void PaintText(Graphics g,RectangleF r,string text,Font font,Color color,bool centered=false)
    {
        using(var b=new SolidBrush(color))using(var f=new StringFormat{Alignment=centered?StringAlignment.Center:StringAlignment.Near,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})
            g.DrawString(text,font,b,r,f);
    }
    void Fill(Graphics g,Color c,int x,int y,int w,int h){using(var b=new SolidBrush(c))g.FillRectangle(b,x,y,w,h);}
    void TogglePaint(Graphics g,int x,bool on)
    {
        using(var path=Theme.Round(new RectangleF(x,546,54,30),15))
        using(var b=new SolidBrush(on?teal:Color.FromArgb(97,109,107))){g.FillPath(b,path);}
        using(var b=new SolidBrush(white))g.FillEllipse(b,x+(on?28:3),549,24,24);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if(Width<2||Height<2||skin==null)return;
        var g=e.Graphics;g.ScaleTransform(Width/1497f,Height/966f);g.TranslateTransform(-45,-12);
        g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.SmoothingMode=SmoothingMode.AntiAlias;
        g.DrawImage(skin,new Rectangle(45,12,1497,966));
        if(!overview){
            Fill(g,white,318,58,1224,866);PaintText(g,new RectangleF(353,75,900,75),page,headline,ink);
            Fill(g,Color.FromArgb(232,238,235),61,217,244,56);
            PaintText(g,new RectangleF(85,217,209,56),"⌂    Overview",bold,ink);
            int idx=Array.IndexOf(pages,page);
            using(var b=new SolidBrush(teal))g.FillRectangle(b,61,220+60*idx,4,49);
        }
        else DrawOverview(g);
        string state=clock.Synchronized?"Connected":clock.PortOpen?"Reading clock":"Disconnected";
        
        PaintText(g,new RectangleF(120,917,178,26),clock.PortOpen?clock.Port+" · "+state:"Preview · Offline",regular,ink);
        
        string footer=DateTime.UtcNow<noticeUntil?notice:clock.Status;
        if(clock.Synchronized && !clock.Busy && DateTime.UtcNow>=noticeUntil)
            footer+="  ·  "+(clock.DisplayMode>=2?"Timer / anniversary preview unavailable":clock.PreviewEpoch.HasValue?"Set-time preview":"Computer-time preview");
        using(var f=new StringFormat{Alignment=StringAlignment.Far,LineAlignment=StringAlignment.Center})
        using(var b=new SolidBrush(muted))g.DrawString(footer,regular,b,new RectangleF(741,931,773,34),f);
        if(keyFocus&&focusIndex>=0){Rectangle r=hits[focusIndex].Bounds;using(var p=new Pen(teal,2)){p.DashStyle=DashStyle.Dot;g.DrawRectangle(p,r);}}
    }
    void DrawOverview(Graphics g)
    {
        DateTime time=clock.PreviewEpoch.HasValue?clock.PreviewEpoch.Value+(DateTime.UtcNow-clock.EpochReceived):DateTime.Now;
        string digits=clock.DisplayMode==1?time.ToString("yyMMdd"):time.ToString(hours24?"HHmmss":"hhmmss");
        bool powered=clock.SwitchState(0x23)!=false;
        for(int i=0;i<6;i++){
            int d=clock.DisplayMode>=2||!powered||(i>3&&!seconds)?10:digits[i]-'0';
            var dest=new Rectangle(centers[i]-43,321,86,125);
            using(var attr=new ImageAttributes()){
                var matrix=new ColorMatrix();matrix.Matrix33=d==10?1f:.1f+.9f*percent/100f;attr.SetColorMatrix(matrix);
                g.DrawImage(faces,dest,86*d,0,86,125,GraphicsUnit.Pixel,attr);
            }
            if(clock.PreviewColor.HasValue&&powered){using(var path=new GraphicsPath()){
                path.AddEllipse(centers[i]-51,452,102,38);
                using(var glow=new PathGradientBrush(path)){glow.CenterColor=System.Drawing.Color.FromArgb(142,clock.PreviewColor.Value);glow.SurroundColors=new[]{System.Drawing.Color.Transparent};g.FillPath(glow,path);}
            }}
        }
        if(clock.DisplayMode>=2||!powered||!seconds){Fill(g,dark,1050,356,25,65);if(!powered||clock.DisplayMode>=2)Fill(g,dark,770,356,25,65);}
         PaintText(g,new RectangleF(371,230,369,32),time.ToString("dddd, MMM d"),bold,white);
        PaintText(g,new RectangleF(1332,204,175,30),clock.Synchronized?"●  Connected":clock.PortOpen?"◌  Reading":"○  Not connected",bold,white);
        PaintText(g,new RectangleF(1366,234,147,27),clock.PortOpen?clock.Port:"Preview mode",regular,white);
        
        using(var p=new Pen(Color.Gray,8)){p.StartCap=p.EndCap=LineCap.Round;g.DrawLine(p,482,562,788,562);}
        int thumb=482+306*percent/100;
        using(var p=new Pen(Color.FromArgb(44,163,151),8)){p.StartCap=p.EndCap=LineCap.Round;g.DrawLine(p,482,562,thumb,562);}
        using(var b=new SolidBrush(white))g.FillEllipse(b,thumb-12,550,24,24);
        PaintText(g,new RectangleF(814,544,73,36),percent+"%",regular,white);
        TogglePaint(g,1014,hours24);TogglePaint(g,1198,seconds);
        PaintText(g,new RectangleF(509,697,143,32),clock.PortOpen?clock.Port:"Choose port",regular,ink);
        PaintText(g,new RectangleF(383,757,294,42),clock.Busy?"Please wait…":clock.PortOpen?"Read settings":"Connect",bold,white,true);
        PaintText(g,new RectangleF(385,850,219,36),clock.Synchronized?"Clock connected":clock.PortOpen?"Reading settings":"Ready for preview",regular,muted);
        PaintText(g,new RectangleF(596,850,116,36),clock.IsBluetooth?"BLE": "115200 baud",regular,muted);
        PaintText(g,new RectangleF(830,692,226,36),edit.ToString("MMM d, yyyy"),regular,ink);
        PaintText(g,new RectangleF(830,737,226,36),edit.ToString("HH:mm"),regular,ink);
        if(scene!=0){
            for(int i=0;i<3;i++){Rectangle r=new Rectangle(1155+i*117,722,116,77);
                using(var path=Theme.Round(r,9))using(var b=new SolidBrush(i==scene?teal:white))g.FillPath(b,path);
                PaintText(g,new RectangleF(r.X,726,116,37),new[]{"☼","☾","◎"}[i],bold,i==scene?white:ink,true);
                PaintText(g,new RectangleF(r.X,764,116,28),new[]{"Daylight","Nightfall","Focus"}[i],regular,i==scene?white:ink,true);
            }
        }
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){tips.Dispose();menu.Dispose();if(picker!=null)picker.Dispose();skin.Dispose();faces.Dispose();regular.Dispose();bold.Dispose();headline.Dispose();}
        base.Dispose(disposing);
    }
}

