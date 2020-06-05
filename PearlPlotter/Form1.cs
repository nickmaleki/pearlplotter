using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using System.IO.Ports;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Drawing.Drawing2D;
using System.Management;
using flash_tool.common.Utils;

namespace PearlPlotter1_3 {
    public partial class Form1 : Form {
        public Form1() {
            InitializeComponent();
        }

        // *** Variables *** // 
        // Plotting
        static int mmToSteps = 100;
        int gridSize = 5;
        enum units { mm, inch, cm };
        int plotterW;
        int plotterH;
        enum shape { Ellipse, Rectangle };
        int shapeX;
        int shapeY;
        int shapeW = 80;
        int shapeH = 80;
        List<Point> pointList = new List<Point>();
        int timeToAlign = 5500;
        int timeToMove = 0;
        int pIndex = 0;

        // Port Acquisition
        string plotterName = "EiBotBoard";
        string sensorManufacturer = "FTDI";
        private SerialPort portP;
        private SerialPort portILT;

        // Graph Variables
        Random rnd = new Random();
        double[,] gPoints;
        Brush brush = new SolidBrush(Color.FromArgb(127, 127, 255));
        int minColor = 255;
        int tmpColor = 0;
        double maxIntensity = 0;

        // Aesthetics
        bool draggable;
        int mouseX;
        int mouseY;
        int rMax = Color.Chocolate.R;
        int rMin = Color.Blue.R;

        int plotDelay = 350;
        enum plotterState { Start, Pause, Stop };
        plotterState pState = plotterState.Stop;
        string fileLocation = Environment.GetFolderPath(Environment.SpecialFolder.Desktop).ToString();

        // Text Boxes
        string fileName = "Untitled";
        string sensorName = "SED270";
        string LEDNum = "3";
        string wavelengthValue = "";

        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.Timer timer2;
        private System.Windows.Forms.Timer timerAutoAlign;

        bool iltWrite = true;
        string iltOut = "-1";
        double iltOutDbl = 0;
        double tempOut;

        bool buttonToggled1 = false;

        private void InitTimers() {
            timer1 = new System.Windows.Forms.Timer();
            timer1.Tick += new EventHandler(timer1_Tick);
            timer1.Interval = 1; // in miliseconds

            timer2 = new System.Windows.Forms.Timer();
            timer2.Tick += new EventHandler(timer2_Tick);
            timer2.Interval = 25;

            timerAutoAlign = new System.Windows.Forms.Timer();
            timerAutoAlign.Tick += new EventHandler(timerAutoAlign_Tick);
            timerAutoAlign.Interval = timeToAlign; // in miliseconds
        }

        private void updateDropDowns() {
            comboBox1.SelectedIndex = 0;
            comboBox3.SelectedIndex = 0;
            comboBox4.SelectedIndex = 0;
        }

        private void timer1_Tick(object sender, EventArgs e) {
            if (pIndex != pointList.Count - 1) {
                double dx = pointList[pIndex + 1].X - pointList[pIndex].X;
                double dy = pointList[pIndex + 1].Y - pointList[pIndex].Y;

                timeToMove = (int)(Math.Sqrt(Math.Pow(dx, 2) + Math.Pow(dx, 2) + 1) * gridSize * mmToSteps * .5); //.5 is a speed modifier .
                timer1.Interval = timeToMove;
                moveP(timeToMove, (int)dx, (int)dy);
                gPoints[pointList[pIndex].X, pointList[pIndex].Y] = getIrrad();
                DrawChart();
                progressBar1.PerformStep();
                pIndex++;
            } else {
                saveCSV();
                savePlot();
                timer1.Stop();
                disableMotors();
                resetVars();
            }

        }

        private void resetVars() {
            pState = plotterState.Stop;
            btnStartPause.BackColor = Color.FromArgb(48, 51, 56);
            btnStartPause.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 44, 47);
            btnStartPause.FlatAppearance.MouseDownBackColor = Color.FromArgb(146, 211, 182);
            btnStartPause.ForeColor = Color.FromArgb(153, 170, 181);
            btnStartPause.Text = "Run";

            pointList = new List<Point>();
            pIndex = 0;
            clearGraph();
            generatePoints();
        }

        private void timerAutoAlign_Tick(object sender, EventArgs e) {
            button1.BackColor = Color.FromArgb(48, 51, 56);
            button1.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 44, 47);
            button1.FlatAppearance.MouseDownBackColor = Color.FromArgb(146, 211, 182);
            button1.ForeColor = Color.FromArgb(153, 170, 181);
            button1.Cursor = Cursors.Hand;
            buttonToggled1 = false;
            timerAutoAlign.Stop();
        }

        private void timer2_Tick(object sender, EventArgs e) {
            if (portILT != null && portILT.IsOpen) {
                if (iltWrite) {
                    try {
                        portILT.Write("getirradiance\r");
                        portILT.DiscardOutBuffer();
                        iltWrite = false;
                    } catch (Exception ex) {
                        Console.WriteLine("write broke");
                    }
                } else {
                    try {
                        iltOut = portILT.ReadExisting();
                        double.TryParse(iltOut, NumberStyles.Float, CultureInfo.InvariantCulture, out tempOut);
                        if (tempOut != -999 && tempOut != 0) {
                            iltOutDbl = tempOut;
                            label15.Text = decimal.Parse(iltOutDbl.ToString(), System.Globalization.NumberStyles.Any).ToString();
                        }
                        iltWrite = true;
                        portILT.DiscardInBuffer();
                    } catch (Exception ex) {
                        Console.WriteLine("read broke");
                    }
                }
            }
        }

        private void Form1_Load(object sender, EventArgs e) {
            //Form Initialization
            getPorts();
            initializeTextBoxes();
            updateDropDowns();
            InitTimers();
            this.MaximizedBounds = Screen.FromHandle(this.Handle).WorkingArea;
            generatePoints();
            initializeTrackBars();
            initializePlot();
            timer2.Start();
        }

        private void initializeTextBoxes() {
            richTextBox8.Text = fileLocation;
        }

        private void initializeProgressBars() {
            progressBar1.Value = 0;
            progressBar1.Maximum = pointList.Count - 1;
            progressBar1.Step = 1;
        }

        private void initializeTrackBars() {
            TrackBar1.Maximum = 179;
            TrackBar1.Minimum = -179;
            TrackBar1.LargeChange = 50;
            TrackBar1.SmallChange = 50;
            TrackBar1.Value = -45;

            TrackBar2.Maximum = 90;
            TrackBar2.Minimum = -90;
            TrackBar2.LargeChange = 1;
            TrackBar2.SmallChange = 1;
            TrackBar2.Value = 45;
        }

        private void initializePlot() {
            var c = Chart1.ChartAreas[0];

            c.AxisX.Title = "X";
            c.AxisX.TitleForeColor = Color.White;
            c.AxisX.MajorGrid.LineColor = Color.White;
            c.AxisX.LineColor = Color.White;
            c.AxisX.Minimum = 0;
            c.AxisX.Maximum = (shapeW / gridSize) + 1;
            c.AxisX.Interval = 1;
            c.AxisX.LabelStyle.ForeColor = Color.White;

            c.AxisY.Title = "Y";
            c.AxisY.MajorGrid.LineColor = Color.White;
            c.AxisY.Minimum = 0;
            c.AxisY.LineColor = Color.White;
            c.AxisY.LabelStyle.ForeColor = Color.White;
            c.AxisY.TitleForeColor = Color.White;

            c.BackColor = Color.Transparent;
            c.BackSecondaryColor = Color.White;
            c.BackGradientStyle = GradientStyle.HorizontalCenter;
            c.BorderColor = Color.White;
            c.BorderDashStyle = ChartDashStyle.Solid;
            c.BorderWidth = 10;
            c.ShadowOffset = 2;

            // Enable 3D charts
            c.Area3DStyle.Enable3D = true;
            c.Area3DStyle.Perspective = 45;
            //c.Area3DStyle.Rotation = 90;

            DrawChart();
        }


        private bool pointIsInEllipse(Ellipse Ellipse, Point location) {
            Point center = new Point((int)(Ellipse.Width / 2), (int)(Ellipse.Height / 2));

            double _xRadius = Ellipse.Width / 2;
            double _yRadius = Ellipse.Height / 2;


            if (_xRadius <= 0.0 || _yRadius <= 0.0) return false;
            /* This is a more general form of the circle equation
             *
             * X^2/a^2 + Y^2/b^2 <= 1
             */

            Point normalized = new Point(location.X - center.X,
                                         location.Y - center.Y);

            return ((double)(normalized.X * normalized.X) / (_xRadius * _xRadius)) + ((double)(normalized.Y * normalized.Y) / (_yRadius * _yRadius)) <= 1.0;
        }

        private double getIrrad() {
            timer1.Interval += plotDelay;
            if (iltOutDbl > maxIntensity) {
                maxIntensity = iltOutDbl;
            }
            return iltOutDbl;
        }

        private void DrawChart() {
            Chart1.Series.Clear();
            Chart1.ChartAreas[0].Area3DStyle.Rotation = TrackBar1.Value;
            Chart1.ChartAreas[0].Area3DStyle.Inclination = TrackBar2.Value;

            for (int i = 0; i < gPoints.GetLength(0); i++) {
                Chart1.Series.Add("z" + i.ToString());

                Chart1.Series[i].ChartType = SeriesChartType.Area;
                Chart1.Series[i].BorderWidth = 0;
                //Chart1.Series[i].Color = Color.SteelBlue;
                Chart1.Series[i].IsVisibleInLegend = false;
                // Set series strip width
                Chart1.Series[i]["PointWidth"] = "1";
                // Set series points gap to 1 pixels
                Chart1.Series[i]["PixelPointGapDepth"] = "1";

                Chart1.Series[i].Points.AddXY(0, gPoints[0, i]);

                Chart1.Series[i].Points[0].Color = Color.FromArgb((int)((((gPoints[0, i] + 1) / (maxIntensity + 1))) * 127) + 127, (int)((((gPoints[0, i] + 1) / (maxIntensity + 1))) * 127) + 127, 255);

                for (int j = 1; j < gPoints.GetLength(1) + 1; j++) {
                    //Chart1.Series[i].Points.AddXY(j, 300);
                    Chart1.Series[i].Points.AddXY(j, gPoints[j - 1, i]);
                    //Chart1.Series[i].Points[j].Color = Color.SteelBlue; // 70 130

                    tmpColor = (int)(((gPoints[j - 1, i] + 1) / (maxIntensity + 1)) * 127) + 127;
                    if (tmpColor > minColor) {
                        minColor = tmpColor;
                    }

                    if (gPoints[j - 1, i] == 0 || gPoints[j - 1, i] == -1) {
                        tmpColor = minColor;
                    }

                    if (tmpColor < 0 || tmpColor > 255) {
                        tmpColor = 255;
                    }
                    Chart1.Series[i].Points[j].Color = Color.FromArgb(tmpColor, tmpColor, 255);
                    //RainbowNumberToColor((float)(gPoints[j, i] / maxIntensity));
                }

            }
        }

        private void moveP(int time, int x, int y) { // Move across grid
            //convert from grid to mm to actual motor steps
            int moveX = -x * gridSize * mmToSteps;
            int moveY = y * gridSize * mmToSteps;

            //Look at distance to move along 45-degree axes, for native motor steps:
            int motorSteps1 = moveX + moveY;
            int motorSteps2 = moveX - moveY;

            try {
                portP.WriteLine("SM," + time + "," + motorSteps1 + "," + motorSteps2 + ".");
            } catch (Exception ex) {
                Console.WriteLine("Error using port: {0}", ex.Message);
            }

        }

        private void moveU(int time, int x, int y) { // Move in mm
            //convert from mm to actual motor steps
            int moveX = -x * mmToSteps;
            int moveY = y * mmToSteps;

            //Look at distance to move along 45-degree axes, for native motor steps:
            int motorSteps1 = moveX + moveY;
            int motorSteps2 = moveX - moveY;

            try {
                portP.WriteLine("SM," + time + "," + motorSteps1 + "," + motorSteps2 + ".");
            } catch (Exception ex) {
                Console.WriteLine("Error using port: {0}", ex.Message);
            }
        }

        private void doPlot() {
            for (int y = 0; y < gPoints.GetLength(0); y++) {
                for (int x = 0; x < gPoints.GetLength(0); x++) {
                    if (gPoints[x, y] != -1) {
                        pointList.Add(new Point(x, y));
                    }
                }
            }
            //timer1.Enabled = true;
            //pointList.RemoveAt(0);
            pointList.Insert(0, new Point(0, 0));
            pointList.Add(new Point(0, 0));
            initializeProgressBars();
            timer1.Start();
        }

        private void btnStartPause_Click(object sender, EventArgs e) {
            if (pState == plotterState.Stop) {
                Console.WriteLine("pState = Stop and btnstartpause clicked");
                pState = plotterState.Start;
                btnStartPause.BackColor = Color.FromArgb(105, 196, 154);
                btnStartPause.FlatAppearance.MouseOverBackColor = Color.FromArgb(251, 184, 72);
                btnStartPause.FlatAppearance.MouseDownBackColor = Color.FromArgb(251, 184, 72);
                btnStartPause.ForeColor = Color.FromArgb(54, 57, 63);
                btnStartPause.Text = "Running";

                //GetPorts
                generatePoints();
                getPorts();
                doPlot();
            } else if (pState == plotterState.Start) {
                pState = plotterState.Pause;
                btnStartPause.BackColor = Color.FromArgb(251, 184, 72);
                btnStartPause.FlatAppearance.MouseOverBackColor = Color.FromArgb(105, 196, 154);
                btnStartPause.FlatAppearance.MouseDownBackColor = Color.FromArgb(105, 196, 154);
                btnStartPause.ForeColor = Color.FromArgb(54, 57, 63);
                btnStartPause.Text = "Paused";
                timer1.Stop();
            } else if (pState == plotterState.Pause) {
                pState = plotterState.Start;
                btnStartPause.BackColor = Color.FromArgb(105, 196, 154);
                btnStartPause.FlatAppearance.MouseOverBackColor = Color.FromArgb(251, 184, 72);
                btnStartPause.FlatAppearance.MouseDownBackColor = Color.FromArgb(251, 184, 72);
                btnStartPause.ForeColor = Color.FromArgb(54, 57, 63);
                btnStartPause.Text = "Running";
                timer1.Start();
            }
        }

        private void button3_Click(object sender, EventArgs e) {
            saveCSV();

            pState = plotterState.Stop;
            btnStartPause.BackColor = Color.FromArgb(48, 51, 56);
            btnStartPause.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 44, 47);
            btnStartPause.FlatAppearance.MouseDownBackColor = Color.FromArgb(146, 211, 182);
            btnStartPause.ForeColor = Color.FromArgb(153, 170, 181);
            btnStartPause.Text = "Run";

            button3.BackColor = Color.FromArgb(240, 71, 71);
            button3.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 71, 71);
            button3.FlatAppearance.MouseDownBackColor = Color.FromArgb(240, 71, 71);
            button3.ForeColor = Color.FromArgb(153, 170, 181);
            button3.Cursor = Cursors.Default;

            resetTimer();
        }

        private void resetTimer() {
            timer1.Stop();
            disableMotors();
        }

        private void generatePoints() {
            if ((gPoints == null) || (gPoints.GetLength(0) != ((shapeW / gridSize) + 1)) || (gPoints.GetLength(1) != ((shapeH / gridSize) + 1))) {
                gPoints = new double[(shapeW / gridSize) + 1, (shapeH / gridSize) + 1];
            }


            for (int y = 0; y < gPoints.GetLength(1); y++) {
                for (int x = 0; x < gPoints.GetLength(0); x++) {
                    gPoints[x, y] = 0;
                    Ellipse myEllipse = new Ellipse();
                    myEllipse.Width = shapeW;
                    myEllipse.Height = shapeH;
                    if (pointIsInEllipse(myEllipse, new Point(x * gridSize, y * gridSize))) {
                        gPoints[x, y] = 0;
                    } else {
                        gPoints[x, y] = -1;
                    }
                }
            }
        }

        private void autoAlign() {
            timerAutoAlign.Start();
            moveU(2000, 0, 200);
            moveU(1500, 150, 0);
            moveU(1000, 0, -144);
            moveU(1000, -shapeW / 2, -shapeH / 2);
        }

        private void button1_Click_1(object sender, EventArgs e) {
            if (buttonToggled1 == false) {
                buttonToggled1 = true;
                button1.BackColor = Color.FromArgb(105, 196, 154);
                button1.FlatAppearance.MouseOverBackColor = Color.FromArgb(105, 196, 154);
                button1.FlatAppearance.MouseDownBackColor = Color.FromArgb(105, 196, 154);
                button1.ForeColor = Color.FromArgb(54, 57, 63);
                button1.Cursor = Cursors.Default;

                getPorts();
                autoAlign();
            }
        }

        private void saveCSV() {
            gPoints[0, 0] = 0;
            String csvLine = "";
            using (var w = new StreamWriter(fileLocation + "\\" + fileName + ".csv")) {
                for (int y = 0; y < gPoints.GetLength(1); y++) {
                    for (int x = 0; x < gPoints.GetLength(0); x++) {
                        if (gPoints[x, y] == -1) {
                            gPoints[x, y] = 0;
                        }
                        csvLine += gPoints[x, y] + ",";
                    }
                    w.WriteLine(csvLine);
                    w.Flush();
                    csvLine = "";
                }
                w.WriteLine("Date/Time: " + "," + DateTime.Now.ToString());
                w.Flush();
                w.WriteLine("LED Wavelength: " + "," + wavelengthValue);
                w.Flush();
                w.WriteLine("Number of LEDs: " + "," + LEDNum);
                w.Flush();
                w.WriteLine("Sensor: " + "," + sensorName);
                w.Flush();
                w.WriteLine("Plot Delay: " + "," + plotDelay);
                w.Flush();
                w.WriteLine("Shape Width: " + "," + shapeW);
                w.Flush();
                w.WriteLine("Shape Height: " + "," + shapeH);
                w.Flush();
                w.WriteLine("Grid Size: " + "," + gridSize);
                w.Flush();
                w.WriteLine("Units: " + "," + comboBox1.Text);
                w.Flush();
                w.WriteLine("File Name: " + "," + fileName);
                w.Flush();
                w.WriteLine("Notes: " + "," + richTextBox11.Text);
                w.Flush();
            }
        }

        public void getPorts() {
            if (!(portP != null && portP.IsOpen)) {
                List<Win32DeviceMgmt.DeviceInfo> tempPorts = Win32DeviceMgmt.GetAllCOMPorts();

                for (int i = 0; i < SerialPort.GetPortNames().Length; i++) {
                    if (tempPorts != null) {
                        if (!tempPorts[i].Equals(null)) {
                            try {
                                if (tempPorts[i].bus_description == plotterName) {
                                    portP = new SerialPort(tempPorts[i].name, 115200, Parity.None, 8, StopBits.One);
                                    break;
                                }
                            } catch (Exception ex) {
                                Console.WriteLine("Error finding Plotter port: " + ex.Message);
                            }
                        }
                    }
                }

                try {
                    portP.Open();
                } catch (Exception ex) {
                    Console.WriteLine("Error opening plotter port: " + ex.Message);
                }
            }

            if (!(portILT != null && portILT.IsOpen)) {
                ManagementObjectSearcher ILTSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%USB Serial Port%'");

                try {
                    foreach (ManagementObject queryObj in ILTSearcher.Get()) {
                        if (queryObj["Manufacturer"].ToString().Contains(sensorManufacturer)) {
                            portILT = new SerialPort(queryObj["Caption"].ToString().Replace("USB Serial Port (", "").Replace(")", ""), 115200, Parity.None, 8, StopBits.One);
                            break;
                        }

                    }
                    Console.WriteLine("ILT Port: " + portILT.PortName);
                } catch (Exception ex) {
                    Console.WriteLine("Error finding ILT port: " + ex.Message);
                    //portILT = new SerialPort("COM9", 115200, Parity.None, 8, StopBits.One);
                }

                try {
                    portILT.Open();
                } catch (Exception ex) {
                    Console.WriteLine("Error opening ILT port: " + ex.Message);
                }
            }
        }

        private void close_Click(object sender, EventArgs e) { //Event for closing the form
            disableMotors();
            Application.Exit();
        }

        private void disableMotors() {
            try {
                portP.WriteLine("EM," + 0 + "," + 0 + ".");
            } catch (Exception ex) {
                Console.WriteLine("Error using port: {0}", ex.Message);
            }

        }

        public void clearGraph() {
            for (int y = 0; y < gPoints.GetLength(0); y++) {
                for (int x = 0; x < gPoints.GetLength(0); x++) {
                    gPoints[x, y] = 0;
                }
            }
        }

        private void savePlot() {
            Chart1.SaveImage(fileLocation + "\\" + fileName + "_" + sensorName + "_" + wavelengthValue + ".png", ChartImageFormat.Png);
        }

        private void button4_Click(object sender, EventArgs e) {
            savePlot();
        }

        private void button5_Click(object sender, EventArgs e) { // Controls the save folder
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            fbd.Description = "Custom Description";

            if (fbd.ShowDialog() == DialogResult.OK) {
                string selectedPath = fbd.SelectedPath;
                richTextBox8.Text = selectedPath;
            }

        }

        private void richTextBox1_TextChanged(object sender, EventArgs e) {
            fileName = tbFileName.Text;
        }

        private void tbDelay_TextChanged(object sender, EventArgs e) {
            int.TryParse(tbDelay.Text, out plotDelay);
        }

        private void Button2_Click(object sender, EventArgs e) {
            generatePoints();
            panel3.Refresh();
            DrawChart();
        }

        private void TrackBar1_Scroll(object sender, EventArgs e) {
            DrawChart();
        }
        private void TrackBar2_Scroll(object sender, EventArgs e) {
            DrawChart();
        }

        private void comboBox3_SelectedIndexChanged(object sender, EventArgs e) {
            wavelengthValue = comboBox3.Text;
        }

        private void richTextBox2_TextChanged(object sender, EventArgs e) {
            int.TryParse(richTextBox2.Text, out gridSize);
            if (gridSize <= 0) {
                gridSize = 1;
            }
            Chart1.ChartAreas[0].AxisX.Maximum = (shapeW / gridSize) + 1;
            DrawChart();
        }

        private void richTextBox8_TextChanged(object sender, EventArgs e) {
            fileLocation = richTextBox8.Text;
        }

        private void richTextBox7_TextChanged(object sender, EventArgs e) {
            int.TryParse(richTextBox7.Text, out shapeW);
            Chart1.ChartAreas[0].AxisX.Maximum = (shapeW / gridSize) + 1;
            DrawChart();
        }

        private void richTextBox6_TextChanged(object sender, EventArgs e) {
            int.TryParse(richTextBox6.Text, out shapeH);
            if (shapeH <= 0) {
                shapeH = 5;
            }
            //DrawChart();
        }

        private void TrackBar2_Scroll_1(object sender, EventArgs e) {
            DrawChart();
        }

        private void TrackBar1_Scroll_1(object sender, EventArgs e) {
            DrawChart();
        }

        // *** Window Aesthetics *** //
        protected override void WndProc(ref Message m) {
            const int wmNcHitTest = 0x84;
            const int htBottomLeft = 16;
            const int htBottomRight = 17;
            if (m.Msg == wmNcHitTest) {
                int x = (int)(m.LParam.ToInt64() & 0xFFFF);
                int y = (int)((m.LParam.ToInt64() & 0xFFFF0000) >> 16);
                Point pt = PointToClient(new Point(x, y));
                Size clientSize = ClientSize;
                if (pt.X >= clientSize.Width - 16 && pt.Y >= clientSize.Height - 16 && clientSize.Height >= 16) {
                    m.Result = (IntPtr)(IsMirrored ? htBottomLeft : htBottomRight);
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private void maximize_Click(object sender, EventArgs e) {
            if (this.WindowState == FormWindowState.Normal) {
                this.WindowState = FormWindowState.Maximized;
            } else {
                this.WindowState = FormWindowState.Normal;
            }

        }
        private void minimize_Click(object sender, EventArgs e) {
            this.WindowState = FormWindowState.Minimized;
        }
        private void panel1_MouseDown(object sender, MouseEventArgs e) {
            draggable = true;
            mouseX = MousePosition.X - this.Left;
            mouseY = MousePosition.Y - this.Top;
        }
        private void panel1_MouseMove(object sender, MouseEventArgs e) {
            if (draggable) {
                this.Top = MousePosition.Y - mouseY;
                this.Left = MousePosition.X - mouseX;

            }
        }
        private void panel1_MouseUp(object sender, MouseEventArgs e) {
            draggable = false;
        }


        // *** Unused *** //
        private void comboBox4_SelectedIndexChanged(object sender, EventArgs e) {

        }

        private void panel3_Paint(object sender, PaintEventArgs e) {
            Graphics g = e.Graphics;

            if ((gPoints == null) || (gPoints.GetLength(0) != ((shapeW / gridSize) + 1)) || (gPoints.GetLength(1) != ((shapeH / gridSize) + 1))) {
                gPoints = new double[(shapeW / gridSize) + 1, (shapeH / gridSize) + 1];
            }


            for (int y = 0; y < gPoints.GetLength(1); y++) {
                for (int x = 0; x < gPoints.GetLength(0); x++) {
                    if (gPoints[x, y] == 0) {
                        g.FillEllipse(Brushes.White, x * 3, y * 3, 3, 3);
                    } else {
                        g.FillEllipse(brush, x * 3, y * 3, 3, 3);
                    }
                }
            }
        }

        private void panel1_Paint(object sender, PaintEventArgs e) {

        }

        private void Chart1_Click(object sender, EventArgs e) {

        }

        private void button1_Click(object sender, EventArgs e) {

        }
        private void progressBar1_Click(object sender, EventArgs e) {
            
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e) {

        }

        private void tableLayoutPanel1_Paint(object sender, PaintEventArgs e) {

        }

        private void Chart1_Click_1(object sender, EventArgs e) {

        }

        private void label1_Click(object sender, EventArgs e) {

        }

        private void label2_Click(object sender, EventArgs e) {

        }

        private void label3_Click(object sender, EventArgs e) {

        }

        private void label4_Click(object sender, EventArgs e) {

        }

        private void label5_Click(object sender, EventArgs e) {

        }

        private void label6_Click(object sender, EventArgs e) {

        }

        private void label7_Click(object sender, EventArgs e) {

        }

        private void label8_Click(object sender, EventArgs e) {

        }

        private void label8_Click_1(object sender, EventArgs e) {

        }

        private void label9_Click(object sender, EventArgs e) {

        }

        private void label13_Click(object sender, EventArgs e) {

        }

        private void label14_Click(object sender, EventArgs e) {

        }

        private void label15_Click(object sender, EventArgs e) {

        }

        private void panel4_Paint(object sender, PaintEventArgs e) {

        }

        private void richTextBox9_TextChanged(object sender, EventArgs e) {
            sensorName = richTextBox9.Text;
        }

        private void label16_Click(object sender, EventArgs e) {

        }

        private void label17_Click(object sender, EventArgs e) {

        }

        private void richTextBox10_TextChanged(object sender, EventArgs e) {
            LEDNum = richTextBox10.Text;
        }

        private void richTextBox11_TextChanged(object sender, EventArgs e) {

        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e) {

        }

        private void btnFullScan_Click(object sender, EventArgs e) {

        }
    }
}
