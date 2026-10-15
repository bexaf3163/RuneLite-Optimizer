using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RuneLiteOptimizer
{
    public sealed class Surface : Panel
    {
        public Surface(){DoubleBuffered=true;BackColor=Color.FromArgb(20,24,31);Padding=new Padding(22);ResizeRedraw=true;}
        protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent==null?Color.FromArgb(11,14,19):Parent.BackColor);using(var path=Geometry.Round(new RectangleF(0,0,Width-1,Height-1),14))using(var brush=new SolidBrush(BackColor))e.Graphics.FillPath(brush,path);}
    }
    public sealed class FrameChart : Control
    {
        public Frame[] Frames=new Frame[0];
        public FrameChart(){DoubleBuffered=true;Height=165;Dock=DockStyle.Top;}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            int left=48,top=14,w=Math.Max(1,Width-left-16),h=Height-38;
            double max=Math.Max(40,Frames.Length==0?40:Math.Min(250,Frames.Max(f=>f.Milliseconds)));
            using(var grid=new Pen(Color.FromArgb(44,51,63)))foreach(double ms in new[]{1000.0/60,1000.0/30}) {float y=top+h-(float)(ms/max*h);g.DrawLine(grid,left,y,left+w,y);g.DrawString(ms.ToString("F1"),Font,Brushes.Gray,2,y-8);}
            if(Frames.Length<2){g.DrawString("График появится после первых кадров",Font,Brushes.Gray,left+12,top+h/2);return;}
            // Max bucket aggregation preserves brief stalls when many frames share a pixel.
            var buckets=new double[w];for(int i=0;i<Frames.Length;i++){int b=Math.Min(w-1,(int)((long)i*w/Frames.Length));buckets[b]=Math.Max(buckets[b],Frames[i].Milliseconds);}
            using(var pen=new Pen(Color.FromArgb(134,151,255),1.7f)) {PointF? previous=null;for(int i=0;i<w;i++) {if(buckets[i]==0)continue;var point=new PointF(left+i,top+h-(float)(Math.Min(max,buckets[i])/max*h));if(previous.HasValue)g.DrawLine(pen,previous.Value,point);previous=point;}}
            g.DrawString("Время кадра, мс · последние 15 с · пики сохранены",Font,Brushes.Gray,left,Height-21);
        }
    }
    public sealed class SessionWindow : Form
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);
        static readonly Color Background=Color.FromArgb(11,14,19),Muted=Color.FromArgb(149,158,174),Accent=Color.FromArgb(146,172,255);
        readonly Capture capture=new Capture();readonly bool smoke; readonly Timer timer=new Timer {Interval=1000};
        readonly Panel body=new Panel {Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(28,20,28,20)};
        readonly Label connection=new Label();readonly List<Button> nav=new List<Button>();
        ComboBox clients,scene,chain;CheckBox auto,active,uncapped; Label fps,low,stalls,assessment,details,state,comparison;FrameChart chart;
        DateTime started;double checkpoint;Statistics baseline;string baselineContext=""; string context="";int page;
        bool hasClient;string configPath;byte[] reviewed;ComboBox renderer,mode;TextBox preview,catalogSearch;DataGridView catalog;
        string sceneName="Grand Exchange"; bool autoEnabled=true,activeOnly=true,testUncapped=true;
        readonly Dictionary<string,string> pendingHd=new Dictionary<string,string>();
        readonly Updates updates=new Updates();Button updateButton,captureButton;Label updateStatus;
        readonly GuidedBenchmark guided=new GuidedBenchmark();readonly GuideHud guideHud=new GuideHud();
        Label guideInstruction,guideStatus;TextBox guideApp;GameProbe latestProbe;bool guidePolling,guideOperation,guideOverlay=true,closingGuide;string guideLaunchError="";
        public SessionWindow(bool smoke)
        {
            this.smoke=smoke;Text="RuneLite Optimizer · 0.5.1";FormBorderStyle=FormBorderStyle.None;BackColor=Background;ForeColor=Color.FromArgb(232,235,243);Font=new Font("Segoe UI",14,FontStyle.Regular,GraphicsUnit.Pixel);MinimumSize=new Size(800,660);Size=new Size(1120,840);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.None;
            var sidebar=new Panel {Dock=DockStyle.Left,Width=162,BackColor=Color.FromArgb(16,20,27),Padding=new Padding(16,24,16,20)};
            sidebar.Controls.Add(TextLabel("RuneLite\nOptimizer",17,ForeColor,DockStyle.Top,86));
            var links=new FlowLayoutPanel {Dock=DockStyle.Top,Height=275,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(0,10,0,0)};
            string[] names={"Обзор","Графика","Система","Обновления","Автоподбор"};for(int i=0;i<names.Length;i++){int index=i;var b=Button(names[i],()=>ShowPage(index));b.Width=128;b.Height=42;links.Controls.Add(b);nav.Add(b);}sidebar.Controls.Add(links);links.BringToFront();
            var foot=TextLabel("ЦЕЛЬ\n60 FPS\n16,67 мс / кадр",11,Muted,DockStyle.Bottom,90);sidebar.Controls.Add(foot);
            updateButton=Button("Версия "+Portable.Version,()=>{if(updates.Ready)InstallUpdate();else ShowPage(3);});updateButton.Dock=DockStyle.Bottom;updateButton.Height=40;sidebar.Controls.Add(updateButton);
            var header=new Panel {Dock=DockStyle.Top,Height=48,Padding=new Padding(28,10,20,8)};var heading=TextLabel("ПРОИЗВОДИТЕЛЬНОСТЬ",10,Muted,DockStyle.Left,28);heading.Width=300;header.Controls.Add(heading);connection.AutoSize=true;connection.Dock=DockStyle.Right;connection.ForeColor=Muted;header.Controls.Add(connection);
            Controls.Add(body);Controls.Add(header);Controls.Add(sidebar);
            var caption=new Panel {Dock=DockStyle.Top,Height=34,BackColor=Color.FromArgb(17,22,30)};
            var captionTitle=TextLabel("   RuneLite Optimizer   /   0.5.1",10,Muted,DockStyle.Left,34);captionTitle.Width=500;caption.Controls.Add(captionTitle);
            captionTitle.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}};
            captionTitle.DoubleClick+=(s,e)=>ToggleMaximize();
            foreach(string symbol in new[]{"×","□","—"}){string action=symbol;var b=Button(symbol,()=>{if(action=="×")Close();else if(action=="—")WindowState=FormWindowState.Minimized;else ToggleMaximize();});b.Dock=DockStyle.Right;b.AutoSize=false;b.Width=42;b.Height=34;b.Padding=Padding.Empty;b.Margin=Padding.Empty;caption.Controls.Add(b);b.BringToFront();}
            Controls.Add(caption);ShowPage(0);
            timer.Tick+=async(s,e)=>{TickSession();updateButton.Text=updates.Ready?"Установить "+updates.AvailableVersion:"Версия "+Portable.Version;if(updateStatus!=null && !updateStatus.IsDisposed)updateStatus.Text=updates.Status;await PollGuide();};if(!smoke)timer.Start();
            Shown+=async(s,e)=>{if(!smoke){if(Updates.Automatic())await updates.Check();else updates.Status="Автообновления отключены";}};
            FormClosed+=(s,e)=>{timer.Stop();capture.Dispose();guideHud.Close();};
            FormClosing+=async(s,e)=>{if(guided.Running&&!closingGuide&&e.CloseReason!=CloseReason.WindowsShutDown){e.Cancel=true;if(MessageBox.Show(this,"Остановить тест и вернуть исходные графические настройки? Результаты уже записанных этапов сохранятся.","Пошаговый тест",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;try{await StopGuide();if(!guided.Running){closingGuide=true;Close();}else MessageBox.Show(this,"Закройте RuneLite. Дождитесь сообщения о возврате графики и затем закройте оптимизатор.");}catch(Exception error){MessageBox.Show(this,error.Message);}}};
        }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if(m.Msg!=0x84 || WindowState!=FormWindowState.Normal)return;
            Point p=PointToClient(new Point(unchecked((short)((long)m.LParam&65535)),unchecked((short)(((long)m.LParam>>16)&65535))));
            bool l=p.X<6,r=p.X>=Width-6,t=p.Y<6,b=p.Y>=Height-6;
            if(t)m.Result=new IntPtr(l?13:r?14:12);else if(b)m.Result=new IntPtr(l?16:r?17:15);else if(l)m.Result=new IntPtr(10);else if(r)m.Result=new IntPtr(11);
        }
        void ToggleMaximize(){MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
        static Label TextLabel(string text,float size,Color color,DockStyle dock,int height){return new Label {Text=text,Font=new Font("Segoe UI",size*1.2f,FontStyle.Regular,GraphicsUnit.Pixel),ForeColor=color,Dock=dock,Height=height,AutoSize=false,TextAlign=ContentAlignment.MiddleLeft};}
        Button Button(string text,Action action){var b=new ModernButton {Text=text,AutoSize=true,Height=38,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(29,35,46),ForeColor=ForeColor,Padding=new Padding(12,6,12,6),Cursor=Cursors.Hand,Margin=new Padding(0,0,10,8)};b.FlatAppearance.BorderSize=0;b.Click+=(s,e)=>{try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"RuneLite Optimizer",MessageBoxButtons.OK,MessageBoxIcon.Information);}};return b;}
        ComboBox Combo(string[] values,int width){var c=new ModernComboBox {DrawMode=DrawMode.OwnerDrawFixed,ItemHeight=22,DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(29,35,46),ForeColor=ForeColor,Width=width,Margin=new Padding(0,2,12,10)};c.DrawItem+=(s,e)=>{e.DrawBackground();using(var brush=new SolidBrush(Color.FromArgb(29,35,46)))e.Graphics.FillRectangle(brush,e.Bounds);string label=e.Index>=0?c.Items[e.Index].ToString():"Не выбран";TextRenderer.DrawText(e.Graphics,label,c.Font,e.Bounds,ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};c.Items.AddRange(values);if(values.Length>0)c.SelectedIndex=0;return c;}
        CheckBox Check(string text,bool value){return new CheckBox {Text=text,Checked=value,AutoSize=true,ForeColor=Muted,Margin=new Padding(0,6,15,10)};}
        FlowLayoutPanel Row(){return new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Padding=new Padding(0,3,0,0)};}
        void Add(Control c){body.Controls.Add(c);c.BringToFront();}
        Label Note(string text,int height){return TextLabel(text,10,Muted,DockStyle.Top,height);}
        void ShowPage(int index)
        {
            if(page==0 && scene!=null && !scene.IsDisposed){sceneName=scene.Text;autoEnabled=auto.Checked;activeOnly=active.Checked;testUncapped=uncapped.Checked;}
            page=index;var previous=body.Controls.Cast<Control>().ToArray();body.Controls.Clear();foreach(var control in previous)control.Dispose();for(int i=0;i<nav.Count;i++)nav[i].BackColor=i==index?Color.FromArgb(49,60,88):Color.FromArgb(24,30,40);
            if(index==0)SessionPage();else if(index==1)SettingsPage();else if(index==2)SystemPage();else if(index==3)UpdatesPage();else GuidePage();
        }
        void SessionPage()
        {
            Add(TextLabel("Плавность под контролем",26,ForeColor,DockStyle.Top,46));
            Add(Note("Последние 30 секунд · цель 60 FPS",30));
            var row=Row();clients=Combo(new string[0],230);row.Controls.Add(clients);var refresh=Button("↻",RefreshClients);refresh.AutoSize=false;refresh.Width=40;row.Controls.Add(refresh);
            scene=Combo(new[]{"Grand Exchange","Varrock","Falador","Lumbridge","Другое место"},165);scene.SelectedItem=sceneName;scene.SelectedIndexChanged+=(s,e)=>ContextChanged();row.Controls.Add(scene);
            captureButton=Button("Начать замер",()=>{if(guided.Running){ShowPage(4);return;}if(capture.Running){auto.Checked=false;capture.Dispose();connection.Text="Измерение остановлено";}else StartCapture();});captureButton.BackColor=Accent;captureButton.ForeColor=Background;row.Controls.Add(captureButton);Add(row);
            var advanced=Row();uncapped=Check("Тест без лимита",testUncapped);uncapped.CheckedChanged+=(s,e)=>ContextChanged();advanced.Controls.Add(uncapped);auto=Check("Автоподключение",autoEnabled);advanced.Controls.Add(auto);active=Check("Только активное окно",activeOnly);advanced.Controls.Add(active);chain=Combo(new string[0],190);advanced.Controls.Add(chain);advanced.Controls.Add(Button("Новый проход",NewPass));advanced.Visible=false;Add(advanced);
            var toggle=Button("Параметры замера  ▾",()=>advanced.Visible=!advanced.Visible);toggle.Dock=DockStyle.Top;toggle.Height=28;toggle.BackColor=Background;toggle.ForeColor=Muted;Add(toggle);
            state=Note("Запустите RuneLite привычным способом. Для сравнения нужен проход от 120 секунд.",36);Add(state);
            var metrics=new TableLayoutPanel {Dock=DockStyle.Top,Height=100,ColumnCount=3,Margin=new Padding(0,10,0,10)};for(int i=0;i<3;i++)metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.333f));
            fps=Metric(metrics,0,"СРЕДНИЙ FPS");low=Metric(metrics,1,"1% LOW");stalls=Metric(metrics,2,"ЗАДЕРЖКИ >50 МС");Add(metrics);
            var graph=new Surface {Dock=DockStyle.Top,Height=172,Padding=new Padding(12)};chart=new FrameChart {Height=145};graph.Controls.Add(chart);Add(graph);
            Add(new Panel {Dock=DockStyle.Top,Height=12});
            var advice=new Surface {Dock=DockStyle.Top,Height=90,Padding=new Padding(18,8,18,8)};assessment=TextLabel("Готов к первому замеру",13,ForeColor,DockStyle.Top,28);details=Note("График покажет не только FPS, но и короткие зависания.",46);advice.Controls.Add(details);advice.Controls.Add(assessment);Add(advice);
            row=Row();row.Controls.Add(Button("Сохранить базовый",SaveBaseline));row.Controls.Add(Button("Сравнить",Compare));row.Controls.Add(Button("Сохранить отчёт",Export));Add(row);comparison=Note("Тот же мир, место и ракурс — для честного сравнения.",32);Add(comparison);
            var more=new Button {Text="Как провести честный тест / доступ к измерениям",Dock=DockStyle.Top,FlatStyle=FlatStyle.Flat,Height=36,ForeColor=Muted};more.FlatAppearance.BorderSize=0;more.Click+=(s,e)=>MessageBox.Show(this,"1. Настройки: выберите GPU или 117 HD и профиль теста 999 FPS. Применение возможно только при закрытой игре.\n2. Запустите игру через своё приложение. В RuneLite проверьте Unlock FPS, VSync Off и отсутствие другого лимита в FPS Control / драйвере.\n3. Grand Exchange, Varrock, Falador, Lumbridge: по 120 секунд с одинаковым миром, ракурсом и окном. Город отмечается вами, автоматически координаты не считываются.\n4. Повторите с целевым профилем 60 FPS. Смотрите p99, 1% low и задержки. Не сравнивайте разные сцены.\n5. Если ETW пишет Access denied: Windows требует Performance Log Users либо ручной запуск оптимизатора от администратора. Автоматического повышения прав нет.\n\nPresentMon измеряет интервалы представления кадров приложения. Это не задержка сети и не гарантия фактической частоты показа монитором.","Проверка плавности");Add(more);
            RefreshClients();
        }
        Label Metric(TableLayoutPanel host,int column,string title){var card=new Surface {Dock=DockStyle.Fill,Margin=new Padding(0,6,10,10),Padding=new Padding(16,10,12,10)};var number=TextLabel("—",27,ForeColor,DockStyle.Top,49);card.Controls.Add(number);card.Controls.Add(TextLabel(title,9,Muted,DockStyle.Top,24));host.Controls.Add(card,column,0);return number;}
        void RefreshClients(){if(clients==null || clients.IsDisposed)return;int old=clients.SelectedItem is Client?((Client)clients.SelectedItem).Id:0;var found=Diagnostics.Clients();clients.Items.Clear();foreach(var c in found)clients.Items.Add(c);if(found.Count>0)clients.SelectedIndex=Math.Max(0,found.FindIndex(c=>c.Id==old));hasClient=found.Count>0;connection.Text=hasClient?"RuneLite найден":"Ожидание RuneLite";}
        void StartCapture(){if(!(clients.SelectedItem is Client))throw new InvalidOperationException("RuneLite не найден. Запустите его через OSRS Path, если используете его плагин.");capture.Start(((Client)clients.SelectedItem).Id);NewPass();}
        void ContextChanged(){checkpoint=LatestTime()+.000001;context=Context();if(state!=null)state.Text="Условия изменены. Начат новый проход; прежние кадры исключены.";}
        string Context(){return scene.Text+" | "+(uncapped.Checked?"uncapped (user declared)":"60 cap (user declared)");}
        double LatestTime(){return capture.Session.LastTime;}
        void NewPass(){checkpoint=LatestTime()+.000001;started=DateTime.UtcNow;context=Context();}
        Frame[] Selected(){string selected=chain.SelectedItem as string;return selected==null?new Frame[0]:capture.Session.Select(selected,checkpoint,active.Checked);}
        void TickSession()
        {
            if(page!=0)
            {
                if(autoEnabled && !capture.Running){var games=Diagnostics.Clients();if(games.Count==1 && (capture.Error.Length==0 || games[0].Id!=capture.Pid))try{capture.Start(games[0].Id);checkpoint=0;started=DateTime.UtcNow;}catch(Exception e){autoEnabled=false;capture.Error=e.Message;}}
                return;
            }
            captureButton.Text=capture.Running?"Остановить":"Начать замер";
            if(!capture.Running && auto.Checked){RefreshClients();if(hasClient && (capture.Error.Length==0 || ((Client)clients.SelectedItem).Id!=capture.Pid))try{StartCapture();}catch(Exception e){auto.Checked=false;capture.Error=e.Message;}}
            string selected=chain.SelectedItem as string;string[] keys=capture.Session.Chains();if(!keys.SequenceEqual(chain.Items.Cast<string>())){chain.Items.Clear();chain.Items.AddRange(keys);if(keys.Length>0)chain.SelectedItem=keys.Contains(selected)?selected:keys[0];}
            var frames=capture.Session.Select(chain.SelectedItem as string,Math.Max(checkpoint,LatestTime()-30),active.Checked);var stats=Statistics.From(frames);
            double passSeconds=capture.Session.Duration(chain.SelectedItem as string,checkpoint,active.Checked);
            fps.Text=stats.Count==0?"—":stats.Fps.ToString("F1");low.Text=stats.Count==0?"—":stats.Low1.ToString("F1");stalls.Text=stats.Count==0?"—":stats.Over50.ToString();
            chart.Frames=frames.Length==0?frames:frames.Where(f=>f.Time>=frames.Last().Time-15).ToArray();chart.Invalidate();
            bool stale=capture.LastFrame!=DateTime.MinValue && (DateTime.UtcNow-capture.LastFrame).TotalSeconds>5;
            connection.Text=capture.Running?"● Измерение":"● Нет потока кадров";
            state.Text=capture.Error.Contains("access denied")?"Windows запретила сбор ETW. Откройте справку о доступе ниже.":!capture.Running && capture.Error.Length>0?"Сбор остановлен. Подробности доступны в экспорте отчёта.":stale?"Нет новых кадров >5 с: возможна пауза, сворачивание или недоступный поток. Это не 0 FPS.":stats.Count==0?"Ожидание кадров активного окна игры. Переключитесь в RuneLite.":string.Format("{0} · {1:F0} с наблюдаемых кадров / 120 с · поток {2}",scene.Text,passSeconds,chain.Text);
            assessment.Text=stats.Count<120?"Нужно больше данных":stats.P99>33.34?"Есть заметные задержки кадров":stats.P99>18?"Для устойчивых 60 остаются просадки":"Выбранный проход укладывается близко к 60 FPS";
            details.Text=stats.Count==0?"117 HD: начните с теней Off, дальности 25 и без расширенной загрузки карты.":string.Format("p95 {0:F1} мс · p99 {1:F1} мс · в бюджете 16,67 мс: {2:F1}%\n{3}",stats.P95,stats.P99,stats.BudgetPercent,stats.P99>18?"Проверьте тени и дальность; измените одну группу и повторите проход.":"Повторите в людном месте и с целевым лимитом 60 FPS.");
            if(capture.Session.Truncated)state.Text="Достигнут лимит 1 млн кадров. Остановите и начните новый сеанс.";
        }
        void SaveBaseline(){var frames=Selected();var candidate=Statistics.From(frames);if(candidate.Seconds<120 || candidate.Count<120)throw new InvalidOperationException("Соберите минимум 120 секунд и 120 кадров в активном окне.");baseline=candidate;baselineContext=Context()+" | "+active.Checked;comparison.Text="Базовый проход сохранён. Измените настройки, вернитесь в то же место и начните новый проход.";}
        void Compare(){var current=Statistics.From(Selected());if(baseline==null)throw new InvalidOperationException("Сначала сохраните базовый проход.");if(current.Seconds<120 || current.Count<120)throw new InvalidOperationException("В новом проходе нужно не менее 120 секунд и 120 кадров.");if(baselineContext!=Context()+" | "+active.Checked)throw new InvalidOperationException("Различаются отмеченная сцена, режим лимита или фильтр фокуса. Сравните одинаковые условия.");comparison.Text=string.Format("Изменение p99: {0:+0.00;-0.00;0} мс · 1% low: {1:+0.0;-0.0;0} FPS.\nПроверьте вручную одинаковые мир, ракурс, размер окна и число игроков.",current.P99-baseline.P99,current.Low1-baseline.Low1);}
        void Export(){using(var dialog=new SaveFileDialog {Filter="Text report|*.txt",FileName="runelite-session.txt"})if(dialog.ShowDialog(this)==DialogResult.OK){var stats=Statistics.From(Selected());File.WriteAllText(dialog.FileName,"RuneLite Optimizer 0.5.1\r\nSource: PresentMon 2.6.0 application present intervals, not display/network latency.\r\nScene/mode manually declared: "+Context()+"\r\nForeground filter: "+active.Checked+"\r\n"+stats.Summary()+"\r\nCapture truncated: "+capture.Session.Truncated+"\r\nNo frames >5s: "+(capture.LastFrame!=DateTime.MinValue&&(DateTime.UtcNow-capture.LastFrame).TotalSeconds>5)+"\r\nCapture diagnostics:\r\n"+capture.Error+"\r\nNo account IDs, process command lines, raw RuneLite settings or game logs included.",new UTF8Encoding(true));}}
        void SettingsPage()
        {
            Add(TextLabel("Настройки графики",24,ForeColor,DockStyle.Top,52));Add(Note("Профили применяются при закрытой игре. Перед записью вы увидите все изменения.",48));
            var row=Row();renderer=Combo(new[]{"117 HD","Standard GPU"},180);mode=Combo(new[]{"Стабильные 60 · минимум нагрузки","Тест запаса · лимит 999 FPS"},320);row.Controls.Add(renderer);row.Controls.Add(mode);row.Controls.Add(Button("Открыть .properties",OpenConfig));Add(row);
            Add(Note("117 HD устанавливается в RuneLite → Plugin Hub → 117 HD. Выберите активный рендерер вручную: запись hd.* не устанавливает и не включает плагин.",60));
            preview=new TextBox {Dock=DockStyle.Top,Height=205,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(23,28,37),ForeColor=ForeColor,BorderStyle=BorderStyle.None,Font=new Font("Consolas",13,FontStyle.Regular,GraphicsUnit.Pixel)};Add(preview);
            renderer.SelectedIndexChanged+=(s,e)=>UpdatePreview();mode.SelectedIndexChanged+=(s,e)=>UpdatePreview();
            row=Row();row.Controls.Add(Button("Применить с резервной копией",ApplyProfile));row.Controls.Add(Button("Откатить…",Restore));Add(row);
            Add(Note("Настройки меняют качество картинки. Снятие лимита используется для теста; после теста верните 60. Проверьте также FPS Control и ограничение драйвера вручную.",57));
            Button applyAdvanced=null;
            var expand=Button("Все параметры 117 HD / поиск",()=>{catalog.Visible=!catalog.Visible;catalogSearch.Visible=catalog.Visible;applyAdvanced.Visible=catalog.Visible;});expand.Dock=DockStyle.Top;Add(expand);
            catalogSearch=new TextBox {Dock=DockStyle.Top,Visible=false,BackColor=Color.FromArgb(29,35,46),ForeColor=ForeColor,BorderStyle=BorderStyle.FixedSingle};Add(catalogSearch);
            catalog=new DataGridView {Dock=DockStyle.Top,Height=330,Visible=false,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,BackgroundColor=Background,BorderStyle=BorderStyle.None,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells,EnableHeadersVisualStyles=false};catalog.DefaultCellStyle.BackColor=Color.FromArgb(23,28,37);catalog.DefaultCellStyle.ForeColor=ForeColor;catalog.DefaultCellStyle.WrapMode=DataGridViewTriState.True;catalog.DefaultCellStyle.SelectionBackColor=Color.FromArgb(49,60,88);catalog.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(29,35,46);catalog.ColumnHeadersDefaultCellStyle.ForeColor=ForeColor;catalog.Columns.Add("name","Параметр");catalog.Columns.Add("value","В файле → план");catalog.Columns.Add("description","Назначение / ключ");catalog.Columns[2].FillWeight=230;Add(catalog);catalogSearch.TextChanged+=(s,e)=>LoadCatalog();catalog.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0)EditHd((string)catalog.Rows[e.RowIndex].Tag);};
            applyAdvanced=Button("Применить выбранные HD-параметры…",ApplyHd);applyAdvanced.Dock=DockStyle.Top;applyAdvanced.Visible=false;Add(applyAdvanced);UpdatePreview();LoadCatalog();
        }
        Dictionary<string,string> Values(){return OptimizationProfiles.Make(renderer.SelectedIndex==0,mode.SelectedIndex==1);}
        void OpenConfig(){using(var d=new OpenFileDialog {Filter="RuneLite settings|*.properties"})if(d.ShowDialog(this)==DialogResult.OK){configPath=d.FileName;reviewed=File.ReadAllBytes(configPath);pendingHd.Clear();UpdatePreview();LoadCatalog();}}
        void UpdatePreview(){var b=new StringBuilder();var current=reviewed==null?new Dictionary<string,string>():Settings.Read(reviewed);b.AppendLine(reviewed==null?"Откройте активный файл профиля RuneLite для предпросмотра.":"Изменения выбранного рендерера:");foreach(var p in Values())b.AppendLine(p.Key+": "+(current.ContainsKey(p.Key)?current[p.Key]:"не задано")+" → "+p.Value);preview.Text=b.ToString();}
        void ApplyProfile(){if(reviewed==null)throw new InvalidOperationException("Сначала откройте активный .properties и проверьте предпросмотр.");if(MessageBox.Show(this,preview.Text+"\nПрименить? Игра должна быть закрыта.","Проверка изменений",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;Changes.Apply(configPath,reviewed,Values());reviewed=File.ReadAllBytes(configPath);UpdatePreview();MessageBox.Show(this,"Профиль сохранён. Запустите RuneLite привычным способом. Резервная копия зашифрована для вашей учётной записи Windows.");}
        void Restore(){using(var d=new OpenFileDialog {Filter="Encrypted backup metadata|*.xml",InitialDirectory=Changes.BackupFolder})if(d.ShowDialog(this)==DialogResult.OK && MessageBox.Show(this,"Восстановить исходный файл? При последующих изменениях откат будет запрещён.","Откат",MessageBoxButtons.YesNo)==DialogResult.Yes){Changes.Restore(d.FileName);reviewed=null;UpdatePreview();}}
        void LoadCatalog(){if(catalog==null)return;catalog.Rows.Clear();string[] lines=Portable.Text("hd-settings.tsv").Split('\n');string text=reviewed==null?"":Settings.Bytes.GetString(reviewed);foreach(string line in lines.Skip(1)){var c=line.Split('\t');if(c.Length<4 || line.IndexOf(catalogSearch.Text,StringComparison.OrdinalIgnoreCase)<0)continue;var matches=System.Text.RegularExpressions.Regex.Matches(text,@"(?m)^\s*"+System.Text.RegularExpressions.Regex.Escape(c[0])+@"\s*[=:]\s*([^\r\n]*)");string value=matches.Count==0?"не задано":matches[matches.Count-1].Groups[1].Value;int n=catalog.Rows.Add(c[1],value+(pendingHd.ContainsKey(c[0])?" → "+pendingHd[c[0]]:""),c[3]+"\n"+c[0]+"\nДвойной щелчок: изменить");catalog.Rows[n].Tag=c[0];}}
        void EditHd(string key)
        {
            if(reviewed==null){MessageBox.Show(this,"Сначала откройте активный .properties.");return;}
            var values=Settings.Read(reviewed);string original=pendingHd.ContainsKey(key)?pendingHd[key]:values.ContainsKey(key)?values[key]:"";
            using(var dialog=new Form {Text=key,Size=new Size(510,230),StartPosition=FormStartPosition.CenterParent,BackColor=Background,ForeColor=ForeColor,Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Padding=new Padding(20)})
            {
                dialog.Controls.Add(TextLabel(key+" · изменение вступит в силу после применения",10,Muted,DockStyle.Top,42));
                string[] options;Control input;
                if(HdRules.Choices.TryGetValue(key,out options)){var c=Combo(options,440);if(options.Contains(original))c.SelectedItem=original;input=c;}
                else {var range=HdRules.Ranges[key];int current;int.TryParse(original,out current);input=new NumericUpDown {Minimum=range[0],Maximum=range[1],Value=Math.Max(range[0],Math.Min(range[1],current)),BackColor=Background,ForeColor=ForeColor,Width=440};}
                input.Location=new Point(20,70);dialog.Controls.Add(input);
                var save=Button("Добавить в план",()=>{string value=input is ComboBox?((ComboBox)input).Text:((NumericUpDown)input).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);if(!HdRules.Valid(key,value))throw new InvalidOperationException("Недопустимое значение.");pendingHd[key]=value;dialog.DialogResult=DialogResult.OK;});save.Location=new Point(20,120);dialog.Controls.Add(save);dialog.AcceptButton=save;
                if(dialog.ShowDialog(this)==DialogResult.OK)LoadCatalog();
            }
        }
        void ApplyHd()
        {
            if(reviewed==null || pendingHd.Count==0)throw new InvalidOperationException("Откройте профиль и добавьте изменения двойным щелчком по параметру.");
            var current=Settings.Read(reviewed);string plan=string.Join("\n",pendingHd.Select(p=>p.Key+": "+(current.ContainsKey(p.Key)?current[p.Key]:"не задано")+" → "+p.Value));
            if(MessageBox.Show(this,plan+"\n\nРасширенные параметры зависят от версии 117 HD. Игра должна быть закрыта. Применить с резервной копией?","План ручных изменений",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            Changes.Apply(configPath,reviewed,new Dictionary<string,string>(pendingHd));reviewed=File.ReadAllBytes(configPath);pendingHd.Clear();UpdatePreview();LoadCatalog();
        }
        void UpdatesPage()
        {
            Add(TextLabel("Всегда актуально",26,ForeColor,DockStyle.Top,50));
            Add(Note("RuneLite Optimizer  "+Portable.Version+"  ·  Portable",40));
            var card=new Surface {Dock=DockStyle.Top,Height=155,Padding=new Padding(20)};
            updateStatus=TextLabel(updates.Status,13,ForeColor,DockStyle.Top,66);card.Controls.Add(updateStatus);
            var row=Row();row.Controls.Add(Button("Проверить сейчас",async()=>{await updates.Check();if(!IsDisposed && updateStatus!=null && !updateStatus.IsDisposed)updateStatus.Text=updates.Status;}));row.Controls.Add(Button("Установить и перезапустить",InstallUpdate));card.Controls.Add(row);row.BringToFront();Add(card);
            var automatic=Check("Автоматически проверять и загружать обновления",smoke?true:Updates.Automatic());automatic.Dock=DockStyle.Top;automatic.Height=46;automatic.CheckedChanged+=async(s,e)=>{try{Updates.SavePreference(automatic.Checked);if(automatic.Checked)await updates.Check();else updates.Status=updates.Ready?updates.Status:"Автообновления отключены";}catch(Exception ex){updateStatus.Text=ex.Message;}};Add(automatic);
            Add(Note("Новая версия загружается с GitHub и проверяется по SHA-256. Установка происходит после нажатия перезапуска. Измерение не прерывается автоматически.",72));
            Add(Note("Один EXE содержит все компоненты. Измеритель кадров и кэш обновлений размещаются в служебной папке Windows. Игровые настройки сохраняются отдельно.",70));
            row=Row();row.Controls.Add(Button("Лицензии",()=>ShowDocument("THIRD-PARTY-NOTICES.md")));row.Controls.Add(Button("Руководство",()=>ShowDocument("README.md")));row.Controls.Add(Button("План оптимизации",()=>ShowDocument("OPTIMIZATION.md")));Add(row);
        }
        void ShowDocument(string resource)
        {
            using(var dialog=new Form {Text="RuneLite Optimizer · "+resource,Size=new Size(760,600),StartPosition=FormStartPosition.CenterParent,BackColor=Background,ForeColor=ForeColor})
            {dialog.Controls.Add(new TextBox {Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,BackColor=Background,ForeColor=ForeColor,Font=Font,Text=Portable.Text(resource).Replace("\n",Environment.NewLine),BorderStyle=BorderStyle.None});dialog.ShowDialog(this);}
        }
        void InstallUpdate()
        {
            if(guided.Running)throw new InvalidOperationException("Сначала завершите или остановите пошаговый тест.");
            if(!updates.Ready)throw new InvalidOperationException("Новая версия пока не загружена. Нажмите «Проверить сейчас».");
            if(MessageBox.Show(this,"Установить версию "+updates.AvailableVersion+" и перезапустить оптимизатор? Текущий замер завершится. Сохраните отчёт, если он нужен. Игра продолжит работать.","Обновление готово",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            updates.Install();timer.Stop();capture.Dispose();Close();
        }
        void GuidePage()
        {
            Add(TextLabel("Графика под ваш ПК",26,ForeColor,DockStyle.Top,48));Add(Note("Три готовых режима для Iris Xe. Примените сразу или уточните настройки замерами.",42));
            guideApp=new TextBox {Dock=DockStyle.Top,BackColor=Background,ForeColor=ForeColor,BorderStyle=BorderStyle.FixedSingle,ReadOnly=true,Text=smoke?"Выберите установленный OSRS Path.exe":GuideApi.Preferences().osrsPath};Add(guideApp);
            var row=Row();row.Controls.Add(Button("Выбрать OSRS Path",()=>{using(var d=new OpenFileDialog {Filter="OSRS Path application|*.exe"})if(d.ShowDialog(this)==DialogResult.OK){guideApp.Text=d.FileName;var pref=GuideApi.Preferences();pref.osrsPath=d.FileName;GuideApi.Save(pref);}}));
            row.Controls.Add(Button("Запустить игру",async()=>{if(guideOperation)return;guideOperation=true;guideLaunchError="";try{guideStatus.Text="Запускаю RuneLite через OSRS Path. Проверяю подключение модуля…";guideInstruction.Text="Дождитесь подтверждения. Запуск может занять до 75 секунд.";string result=await GuideApi.Launch(guideApp.Text);if(!IsDisposed&&guideStatus!=null&&!guideStatus.IsDisposed)guideStatus.Text=result;}catch(Exception error){guideLaunchError=error.Message;if(!IsDisposed&&guideStatus!=null&&!guideStatus.IsDisposed)guideStatus.Text=error.Message;}finally{guideOperation=false;}}));Add(row);
            var card=new Surface{Dock=DockStyle.Top,Height=180,Padding=new Padding(20)};guideStatus=TextLabel(guided.Status,12,Accent,DockStyle.Top,48);guideInstruction=TextLabel(guided.Instruction,13,ForeColor,DockStyle.Top,94);card.Controls.Add(guideInstruction);card.Controls.Add(guideStatus);Add(card);
            row=Row();foreach(string mode in new[]{"none","hd","gpu"}){string selectedMode=mode;row.Controls.Add(Button(mode=="none"?"Стандартная":mode=="hd"?"117 HD":"GPU",async()=>{try{await StartGuide(selectedMode,true);}catch(Exception error){MessageBox.Show(this,error.Message);}}));}Add(row);
            row=Row();row.Controls.Add(Button("Автоподбор по замерам",async()=>{try{await StartGuide();}catch(Exception error){MessageBox.Show(this,error.Message);}}));row.Controls.Add(Button("Остановить / вернуть графику",async()=>{try{await StopGuide();}catch(Exception error){MessageBox.Show(this,error.Message);}}));Add(row);
            var overlay=Check("Краткая подсказка поверх игры",guideOverlay);overlay.Dock=DockStyle.Top;overlay.Height=36;overlay.CheckedChanged+=(s,e)=>{guideOverlay=overlay.Checked;if(!guideOverlay)guideHud.Hide();};Add(overlay);
            Add(Note("Lumbridge → Varrock → Grand Exchange → Falador. В каждом месте: стойте, вращайте камеру, пройдите туда и обратно. Прибытие, движение, камера, мир и размер окна проверяются автоматически.",75));
            Add(Note("До 6 проходов: исходный, лестница качества и повторная проверка с лимитом 72 FPS. Между профилями закройте игру: графика меняется автоматически, клиент запускается снова. Файлы OSRS Path не изменяются.",78));
            row=Row();row.Controls.Add(Button("Открыть отчёты",()=>{string folder=Path.Combine(GuideApi.Folder,"Reports");Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}));Add(row);
        }
        async Task StartGuide(string target=null,bool quick=false)
        {
            if(guideOperation||guided.Running)return;guideOperation=true;
            try
            {
                var p=await GuideApi.Read();if(!p.inGame&&!quick)throw new InvalidOperationException("Нажмите Play и войдите в игру. Затем выберите режим подбора.");
                string selected=target??GuideApi.Preferences().mode;
                var plans=quick?AutoQuality.Description(selected):selected=="none"?AutoQuality.Description(selected):"Исходная графика → высокое качество → баланс → плавность → минимальная нагрузка. После первого успешного уровня — повторный тест с лимитом 72 FPS. Учитываются тени, сглаживание, дальность, загрузка карты, разрешение, фильтрация и освещение. Цвета, доступность, игровой интерфейс и остальные плагины сохраняются.";
                if(MessageBox.Show(this,(quick?"Применить стартовый профиль для i5-1135G7 / Iris Xe без замеров? Это рекомендация, стабильные 60 FPS не проверены. Режим ":"Разрешить автоматический подбор графики в режиме ")+selected+"?\n\n"+plans+(quick?"\n\nЗакройте RuneLite. Настройки применятся автоматически, клиент запустится снова. Переключаются только GPU / 117 HD и глобальный лимит FPS Control. Стандартный режим ограничен примерно 50 FPS. Настройки OSRS Path не меняются.":"\n\nFPS Control: глобальный лимит отключается. Переключаются только GPU и 117 HD; другие плагины сохраняются. Между проходами закройте RuneLite самостоятельно, настройки применятся после полного закрытия. Клиент запустится снова. Вход, движения и камера — ваши. Финальная проверка GPU/HD: лимит 72 FPS, цель 1% low ≥60. Исходный набор зашифрую. До 48 минут замеров плюс переходы."),"План автоматического подбора",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
                if(!quick){capture.Start(p.pid);await Task.Delay(700);if(!capture.Running)throw new InvalidOperationException("ETW не начал запись. Проверьте права сбора кадров; графика ещё не изменена.");capture.Dispose();}guided.Begin(p,Path.Combine(GuideApi.Folder,"Reports"),selected,quick);var prefs=GuideApi.Preferences();prefs.mode=selected;GuideApi.Save(prefs);guided.Save(capture.Session);autoEnabled=false;if(auto!=null&&!auto.IsDisposed)auto.Checked=false;latestProbe=p;
                guideStatus.Text=guided.Status;guideInstruction.Text=guided.Instruction;
            }
            finally{guideOperation=false;}
        }
        Task StopGuide()
        {
            if(guideOperation)return Task.FromResult(0);
            if(guided.Recovery==null&&File.Exists(TuneFiles.Record))guided.Recovery=TuneFiles.Load();
            if(guided.Recovery==null)return Task.FromResult(0);
            guided.Running=true;guided.RestoreRequested=true;guided.RequireClose();guided.Status="Возврат графики · закройте игру";guided.Instruction="Закройте RuneLite и Java-приложения. Исходные настройки восстановятся автоматически; клиент не закроется принудительно.";return Task.FromResult(0);
        }
        void GuideLabels()
        {
            if(guideStatus!=null&&!guideStatus.IsDisposed){guideStatus.Text=guided.Status;guideInstruction.Text=guided.Error.Length>0?guided.Error:guided.Instruction;}
        }
        async Task PollGuide()
        {
            if(smoke||IsDisposed||guidePolling||guideOperation||(!guided.Running&&page!=4))return;guidePolling=true;
            try
            {
                if(guided.Running&&guided.WaitingForClose)
                {
                    GuideLabels();guideHud.Hide();capture.Dispose();
                    if(!await Task.Run(()=>TuneFiles.Offline()))return;
                    guideOperation=true;
                    try
                    {
                        guided.Save(capture.Session);capture.Dispose();
                        if(guided.RestoreRequested){await Task.Run(()=>TuneFiles.Restore(guided.Recovery));guided.Running=false;guided.NeedsApply=guided.WaitingForClose=false;guided.Status="Исходная графика восстановлена";guided.Instruction="Записанные результаты сохранены. Теперь можно закрыть оптимизатор или начать новый подбор.";guided.Save(capture.Session);GuideLabels();return;}
                        await Task.Run(()=>TuneFiles.ApplyOffline(guided.Recovery,guided.Plan(guided.Cycle)));
                        guided.WaitingForClose=false;guided.WaitingForRestart=true;guided.Status="Запуск следующего профиля";guided.Instruction="После запуска нажмите Play. Верните прежний мир и размер окна; замеры продолжатся автоматически.";GuideLabels();
                        string app=GuideApi.Preferences().osrsPath;await GuideApi.Launch(app);
                    }
                    catch(Exception error){guided.Running=false;guided.Error="Автопереход остановлен: "+error.Message+" Резервная копия сохранена; доступна кнопка возврата графики.";guided.Save(capture.Session);GuideLabels();}
                    finally{guideOperation=false;}
                    return;
                }
                var p=await GuideApi.Read();if(IsDisposed||Disposing||guideOperation)return;latestProbe=p;
                if(guided.Running)
                {
                    if(guided.WaitingForRestart)
                    {
                        if(!p.inGame&&!guided.QuickApply){guided.Instruction="Нажмите Play и войдите в игру. После входа тест продолжится автоматически.";GuideLabels();return;}
                        guided.Reconnected(p);if(guided.QuickApply){guided.Running=false;guided.Completed=true;guided.Recovery.Completed=true;TuneFiles.Save(guided.Recovery);guided.Status="Стартовый профиль применён · без замеров";guided.Instruction=guided.Renderer=="none"?"Стандартная графика: GPU и 117 HD отключены. Предел около 50 FPS.":"Профиль для Iris Xe активен. Цель 60+ FPS пока не проверена. Для проверки нажмите «Автоподбор по замерам».";guided.Save(capture.Session);GuideLabels();return;}capture.Start(p.pid);await Task.Delay(700);if(!capture.Running)throw new InvalidOperationException("ETW не начал запись. Проверьте права сбора кадров.");
                    }
                    if(capture.Pid!=p.pid||!capture.Running)guided.Pause("Измеритель кадров остановлен. Проверьте доступ ETW; данные не подменяются фиктивными значениями.");
                    else guided.Observe(p,capture.Session,RuneLiteOptimizer.Capture.Foreground(p.pid));
                }
                else if(guided.OriginalRaw==null){guided.Status=p.inGame?"Игра подключена · "+p.renderer+" · мир "+p.world:"Нажмите Play в RuneLite";guided.Instruction=p.inGame?"Выберите режим: Стандартная, 117 HD или GPU. Подбор начнётся после подтверждения.":"Вход выполняется обычным способом.";}
                GuideLabels();
                if(guided.Running&&!guided.WaitingForClose&&guideOverlay&&RuneLiteOptimizer.Capture.Foreground(p.pid)){guideHud.UpdateText(guided.Status,guided.Instruction);guideHud.PlaceFor(p.pid);if(!guideHud.Visible)guideHud.Show();}else guideHud.Hide();
            }
            catch(Exception error){if(!guideHud.IsDisposed)guideHud.Hide();if(guided.Running){guided.Pause("Ожидание подключения к игре: "+error.Message);GuideLabels();}else if(guideStatus!=null&&!guideStatus.IsDisposed)guideStatus.Text=guideLaunchError.Length>0?guideLaunchError:"Модуль не подключён. Сначала запустите клиент кнопкой выше. "+error.Message;}
            finally{guidePolling=false;}
        }
        void SystemPage()
        {
            Add(TextLabel("Система",24,ForeColor,DockStyle.Top,52));Add(Note("Аппаратные данные помогают выбрать первый профиль. Причину просадок подтверждают повторные замеры.",55));
            var output=new TextBox {Dock=DockStyle.Top,Height=310,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(23,28,37),ForeColor=ForeColor,BorderStyle=BorderStyle.None};
            var row=Row();row.Controls.Add(Button("Проверить компьютер",async()=>{output.Text="Считывание оборудования…";try{output.Text=await Task.Run(()=>Diagnostics.Report());}catch(Exception e){output.Text=e.Message;}}));Add(row);Add(output);
            Add(Note("Если FPS низкий постоянно: сначала тени, сглаживание, дальность и разрешение. Если задержки при движении: сравните загрузку карты. Если хуже только в толпе: проверьте дальность деталей. Меняйте одну группу за проход.",85));
            Add(Note("График измеряет представление кадров. GPU load, температура, сетевой ping и игровые координаты здесь не измеряются. Нельзя гарантировать отсутствие всех зависаний только настройками графики.",75));
        }
        public void RenderSmoke(string folder)
        {
            Directory.CreateDirectory(folder);ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-20000,-20000);Show();
            auto.Checked=false;scene.SelectedItem="Varrock";ShowPage(1);ShowPage(0);
            if(auto.Checked || scene.Text!="Varrock")throw new Exception("Session preferences lost during navigation");
            double t=0;for(int i=0;i<7500;i++){double ms=i%1000==0?75:16.5;t+=ms/1000;capture.Session.Add(new Frame {Time=t,Milliseconds=ms,Chain="test",Active=true});}TickSession();checkpoint=0;SaveBaseline();NewPass();bool rejected=false;try{Compare();}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Empty candidate accepted");capture.Session.Clear();baseline=null;scene.SelectedItem="Grand Exchange";
            foreach(var size in new[]{new Size(1120,840),new Size(800,660)})for(int i=0;i<5;i++)
            {
                Size=size;ShowPage(i);PerformLayout();Application.DoEvents();Snapshot(Path.Combine(folder,"ui-"+size.Width+"-"+i+".png"));
                if(i<2||i==4){body.AutoScrollPosition=new Point(0,10000);Application.DoEvents();Snapshot(Path.Combine(folder,"ui-"+size.Width+"-"+i+"-bottom.png"));}
                if(i==1){catalog.Visible=true;catalogSearch.Visible=true;body.AutoScrollPosition=new Point(0,10000);Application.DoEvents();if(catalog.Rows.Count!=100)throw new Exception("HD catalogue incomplete");Snapshot(Path.Combine(folder,"ui-"+size.Width+"-hd-expanded.png"));}
            }
            File.WriteAllText(Path.Combine(folder,"ui-test-result.txt"),"PASS: navigation preserves collection preferences; 120-second baseline; empty comparison rejected; 100 HD controls; top/bottom and expanded renders at 1120x840 and 800x660. No game interaction.");
            Close();
        }
        void Snapshot(string path){using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(Point.Empty,Size));b.Save(path);}}
    }
}
