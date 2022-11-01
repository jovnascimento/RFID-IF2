using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Intermec.DataCollection.RFID;

namespace RFID
{
    public partial class Form1 : Form
    {
        public BRIReader brdr = null;
        private bool bReaderOffline = true;
        private int tagCount = 0;
        private string[] MyTagList = new string[100];
        public Form1()
        {
            InitializeComponent();
        }

        private bool OpenReaderConnection()
        {
            bool status = false;
            string msg = null;
            string connect = null;

            if (radioButton3.Checked == true)
            {
                //serial
                connect = textBox3.Text;
            }
            else
            {
                //tcpip
                connect = textBox4.Text;
            }

            try
            {
                brdr = new BRIReader(this, connect);

                status = true;
            }
            catch (BasicReaderException ex)
            {
                MessageBox.Show(ex.ToString());
                status = false;
            }

            if (brdr == null || status == false)
            {
                //failed to create reader connection
                bReaderOffline = true;
                PostMessageToListBox1("Não foi possível conectar ao receptor");
                PostMessageToListBox1(connect);
                return false;
            }

            this.brdr.SetAutoPollTriggerEvents(false);

            msg = this.brdr.Execute("PING");
            if (msg != null)
            {
                if (msg.Equals("OK>"))
                {
                    msg = this.brdr.Execute("VER");
                    ParseResponse(msg);

                    bReaderOffline = false;
                    status = true;
                }
            }

            if (status == false)
            {
                //not connected to reader
                PostMessageToListBox1("Não foi possível conectar ao receptor");
                PostMessageToListBox1(connect);
                bReaderOffline = true;
            }

            return status;
        }

        private void ParseResponse(string msg)
        {
            int x = 0;
            string delimStr = null;
            string[] tList = null;
            char[] delimiter = null;
            int count = 0;

            delimStr = "\n";
            delimiter = delimStr.ToCharArray();
            tList = msg.Replace("\r\n", "\n").Split(delimiter);
            count = tList.Length;
            for(x = 0; x < count; x++)
            {
                PostMessageToListBox1(tList[x]);
            }
        }

        private void CloseReaderConnection()
        {
            if (brdr != null)
            {
                brdr.Dispose();
                brdr = null;
            }
        }
        private void ReadTags()
        {
            //Simple read

            bool status = false;

            if (bReaderOffline == true)
            {
                PostMessageToListBox1("Reader is offline");
                return;
            }

            tagCount = 0;

            status = brdr.Read();

            //get the tag ids
            LoadTagList();
        }
        private void LoadTagList()
        {
            if (brdr.TagCount > 0)
            {
                foreach (Tag tt in brdr.Tags)
                {
                    MyTagList[++tagCount] = tt.ToString();

                    if (tt.TagFields.ItemCount > 0)
                    {
                        foreach (TagField tf in tt.TagFields.FieldArray)
                        {
                            MyTagList[tagCount] += " " + tf.ToString();
                        }
                    }
                    PostMessageToListBox1(MyTagList[tagCount]);
                }
            }
            else
            {
                PostMessageToListBox1("NO TAGS");
            }
        }
        private void ReadTagsReportNo()
        {
            //Continuous read using polling to retrieve tag list

            bool status = false;

            if (bReaderOffline == true)
            {
                PostMessageToListBox1("Reader is offline");
                return;
            }

            tagCount = 0;

            //determine how much time you want before polling for tag list.
            //here it is set to 1 second
            timer1.Interval = 5000;

            //Here are various read options
            //Pick one and comment out the other two

            //1. Read epc id's
            status = brdr.StartReadingTags(null, null, BRIReader.TagReportOptions.POLL);

            //2. Use a filter to read only tags who's epc id starts with hex 0102
            //bStatus = brdr.StartReadingTags("HEX(1:4,2)=H0102", null, BRIReader.TagReportOptions.POLL);

            //3. Return the antenna that read the tag and the number of times each tag was read
            //bStatus = brdr.StartReadingTags(null, "ANT COUNT", BRIReader.TagReportOptions.POLL);

            //enable timer to poll for tags
            timer1.Enabled = true;
        }
        private void timer1_Tick(object sender, EventArgs e)
        {
            //enabled by ReadTagsReportNo() function
            //timer used to poll for tags

            bool bStatus = false;

            if (bReaderOffline == true) { return; }

            timer1.Enabled = false;

            bStatus = brdr.PollTags();

            //get the tag ids
            LoadTagList();

            //you must issues a READ STOP to turn off the RF when you are done reading.
            //normally you would not stop at this point but would continue to polling for tags
            //but for this sample we are going to stop reading tags here.
            brdr.StopReadingTags();

            //normally you would re-enable timer and continue polling for tags
            //timer1.Enabled = true;
        }
        private void PostMessageToListBox1(string sMsg)
        {
            listBox1.Items.Add(sMsg);
            listBox1.SelectedIndex = listBox1.Items.Count - 1;
            listBox1.Refresh();
        }

        private void Button1_Click(object sender, EventArgs e)
        {
            //botão para conectar com o IF2

            bool status = true;
            button1.Enabled = false;

            status = OpenReaderConnection();

            if (status == false)
            {
                //não foi possível conectar com o IF2
                MessageBox.Show("Não foi possível conectar");
                button1.Enabled = true;
            }
            else
            {
                //Precisa ser feito para a leitura contínua
                //(AddRFIDEventHandlers, EventHandlerTag,
                //ReadTagsReportEvent e timer1_Tick 
                //AddRFIDEventHandlers();

                PostMessageToListBox1("IF2 conectado!");

                //enable READ button
                button2.Enabled = true;
            }
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            //botão para ler o RFID
            if (bReaderOffline == true)
            {
                PostMessageToListBox1("Receptor está offline");
                return;
            }

            //ReadTags();
            ReadTagsReportNo();
        }
    }
}
