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
using Newtonsoft.Json;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace RFID
{
    public partial class Form1 : Form
    {
        public BRIReader brdr = null;
        private bool bReaderOffline = true;
        private int tagCount = 0;
        private string[] MyTagList = new string[100];
        private string tag;

        public Form1()
        {
            InitializeComponent();
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            //comunicar com o ms de log para pegar o log
        }

        //RabbitMQ
        private static void RMQ_Send(string sql, string queue)
        {
            var factory = new ConnectionFactory()
            {
                HostName = "localhost"
            };
            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                channel.QueueDeclare(queue: queue,
                                     durable: false,
                                     exclusive: false,
                                     autoDelete: false,
                                     arguments: null);

                string message = sql;
                var body = Encoding.UTF8.GetBytes(message);

                channel.BasicPublish(exchange: "",
                                     routingKey: queue,
                                     basicProperties: null,
                                     body: body);
            }
        }

        private static void Logs()
        {
            var factory = new ConnectionFactory()
            {
                HostName = "localhost"
            };

            using (var connection = factory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                var replyQueue = channel.QueueDeclare(queue: "", exclusive: true);
                channel.QueueDeclare(queue: "logs",
                                     durable: false,
                                     exclusive: false,
                                     autoDelete: false,
                                     arguments: null);

                var consumer = new EventingBasicConsumer(channel);

                consumer.Received += (model, ea) =>
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                };

                channel.BasicConsume(queue: replyQueue,
                                     autoAck: true,
                                     consumer: consumer);

                var message_req = "select * from select_tag_log()";
                var body_req = Encoding.UTF8.GetBytes(message_req);

                var properties = channel.CreateBasicProperties();
                properties.ReplyTo = replyQueue.QueueName;
                properties.CorrelationId = Guid.NewGuid().ToString();

                channel.BasicPublish("", "logs", properties, body_req);
            }
        }

        private bool OpenReaderConnection()
        {
            //inicializa variáveis necessárias
            bool status = false;
            string msg = null;
            string connect = null;

            //verifica qual forma de conexão será usada
            if (radioButton3.Checked == true)
            {
                //serial
                connect = textBox3.Text;
            }
            else
            {
                //tcp/ip
                connect = textBox4.Text;
            }

            try
            {
                //tenta criar uma nova conexão, passando o endereço
                brdr = new BRIReader(this, connect);
                status = true;
            }
            catch (BasicReaderException ex)
            {
                MessageBox.Show(ex.ToString());
                status = false;
            }
            //se não foi possível criar uma conexão e o status permaneceu falso
            //a conexão falhou
            if (brdr == null || status == false)
            {
                bReaderOffline = true;
                PostMessageToListBox1("Não foi possível conectar ao receptor");
                PostMessageToListBox1("Endereço: " + connect);
                return false;
            }

            //this.brdr.SetAutoPollTriggerEvents(false);

            //ao inicializar a conexão, primeiro executa um ping no receptor
            msg = this.brdr.Execute("PING");
            if (msg != null)
            {
                //se a mensagem recebida for a padrão
                if (msg.Equals("OK>"))
                {
                    //executa a verificação de versão e imprime a mensagem na caixa de mensagem
                    msg = this.brdr.Execute("VER");
                    ParseResponse(msg);

                    //então, define que o receptor está online
                    bReaderOffline = false;
                    status = true;
                }
            }

            //se o status for falso após todo o processo, não foi possível conectar ao receptor
            if (status == false)
            {
                PostMessageToListBox1("Não foi possível conectar ao receptor");
                PostMessageToListBox1(connect);
                bReaderOffline = true;
            }

            return status;
        }

        //método tratar a mensagem enviada pelo IF2 e imprimí-la na caixa de mensagem
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

        //terminar a conexão com o IF2
        private void CloseReaderConnection()
        {
            if (brdr != null)
            {
                brdr.Dispose();
                brdr = null;
            }
        }

        //método para uma leitura simples das tags
        //ao abertar o botão de "Leitura simples", será feita a leitura de todas as tags próximas
        //instantaneamente, sem uma poll ou leitura contínua
        private void ReadTags()
        {
            bool status = false;

            //verificação se o receptor está online
            if (bReaderOffline == true)
            {
                PostMessageToListBox1("Receptor está offline");
                return;
            }

            tagCount = 0;

            //chama a função de leitura na antena
            status = brdr.Read();

            //chama o método para pegar os IDs das tags
            LoadTagList();
        }

        //método que busca os IDs definidos nas tags e imprime na caixa de mensagem
        private void LoadTagList()
        {
            if (brdr.TagCount > 0)
            {
                //para cada tag identificada
                foreach (Tag tt in brdr.Tags)
                {
                    //lista recebe as tags transformadas em string
                    MyTagList[++tagCount] = tt.ToString();

                    //se as tags possuirem alguma informação
                    if (tt.TagFields.ItemCount > 0)
                    {
                        //para cada tag, busca o TagField, que é o ID
                        foreach (TagField tf in tt.TagFields.FieldArray)
                        {
                            MyTagList[tagCount] += " " + tf.ToString();
                        }
                    }
                    //imprime as tags na caixa de mensagem
                    PostMessageToListBox1(MyTagList[tagCount]);

                    if (radioButton1.Checked)
                    {
                        //faz a inserção no banco
                        //para isso, é preciso corresponder o id com o produto
                        tag = MyTagList[tagCount].Substring(MyTagList[tagCount].Length - 3);
                        int sc = int.Parse(tag);

                        switch (sc)
                        {
                            case 081:
                                string tag = "select * from insert_tag('" + MyTagList[tagCount] + "','luva')";
                                RMQ_Send(tag, "tags");
                                string log = "select * from insert_tag('" + MyTagList[tagCount] + "','luva'," + DateTime.Now + ")";
                                RMQ_Send(log, "logs");
                                //mudar o logs para logs_insert
                                //logs é para receber e enviar o log
                                break;

                            case 082:
                                tag = "select * from insert_tag('" + MyTagList[tagCount] + "','parafuso')";
                                RMQ_Send(tag, "tags");
                                break;

                            case 084:
                                tag = "select * from insert_tag('" + MyTagList[tagCount] + "','chicote')";
                                RMQ_Send(tag, "tags");
                                break;
                        }
                    }
                    else
                    {
                        string tag = "select * from delete_tag('" + MyTagList[tagCount] + "')";
                        RMQ_Send(tag, "tags");
                    }
                }
            }
            //caso não tenha/seja possível identificar nenhuma tag, imprime "NO TAGS"
            else
            {
                PostMessageToListBox1("NO TAGS");
            }
        }
        
        //leitura contínua das tags definido por um determinado período de tempo em ms
        //ao final da leitura, imprime todas as tags na caixa de mensagem
        private void ReadTagsReportNo()
        {
            bool status = false;

            //verificação se o receptor está online
            if (bReaderOffline == true)
            {
                PostMessageToListBox1("Receptor está offline");
                return;
            }

            tagCount = 0;

            //determina o período de tempo em ms para a leitura
            //1000 ms = 1 segundo
            timer1.Interval = 10000;

            //StartReadingTags recebe que a leitura será feita por poll
            status = brdr.StartReadingTags(null, null, BRIReader.TagReportOptions.POLL);

            //inicializa o timer
            timer1.Enabled = true;
        }

        //método que define como o timer deve agir após ser inicializado
        private void timer1_Tick(object sender, EventArgs e)
        {
            bool status = false;

            //verificação se o receptor está online
            //não é necessário imprimir nada pois esse aviso só é necessário quando as função são ativadas
            if (bReaderOffline == true)
            { 
                return; 
            }

            timer1.Enabled = false;

            status = brdr.PollTags();

            LoadTagList();

            //após o período determinado, a leitura é interrompida
            brdr.StopReadingTags();
        }

        //método para postar as mensagens na caixa de mensagem
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
                button3.Enabled = true;
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

            ReadTagsReportNo();
        }
        private void button3_Click(object sender, EventArgs e)
        {
            //botão para ler o RFID
            if (bReaderOffline == true)
            {
                PostMessageToListBox1("Receptor está offline");
                return;
            }

            ReadTags();
        }

        /*SQL
        private void Select()
        {
            try
            {
                conn.Open();
                sql = @"select * from select_tag_log()";
                cmd = new NpgsqlCommand(sql, conn);
                dt = new DataTable();
                dt.Load(cmd.ExecuteReader());
                conn.Close();
                dataGridView1.DataSource = null;
                dataGridView1.DataSource = dt;
            }catch(Exception e)
            {
                conn.Close();
                MessageBox.Show("Erro: " + e);
            }
        }*/
    }
}
