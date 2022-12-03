using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RFID
{
    public partial class F_Window : Form
    {
        public F_Window(string message, string name)
        {
            this.Name = name;
            InitializeComponent();

            dataGridView1.DataSource = JsonConvert.DeserializeObject<DataTable>(message);
            label2.Text = dataGridView1.RowCount.ToString();
        }
    }
}
