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
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

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
        private void Form1_Load(object sender, EventArgs e)
        {
            //comunicar com o ms de log para pegar o log
        }

        //RabbitMQ
        /*private static void RMQ_Send(string sql, string queue)
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

                string messageSend = sql;
                var bodySend = Encoding.UTF8.GetBytes(messageSend);

                channel.BasicPublish(exchange: "",
                                     routingKey: queue,
                                     basicProperties: null,
                                     body: bodySend);
            }
        }*/

        private static void RMQ_RR()
        {
            var factory = new ConnectionFactory()
            {
                HostName = "localhost"
            };
            var connection = factory.CreateConnection();
            var channel = connection.CreateModel();

            var replyQueue = channel.QueueDeclare(
                queue: "",
                exclusive: true);

            channel.QueueDeclare(
                "logs",
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            var consumer = new EventingBasicConsumer(channel);

            consumer.Received += (model, ea) =>
            {
                var bodyReceive = ea.Body.ToArray();
                var messageReceive = Encoding.UTF8.GetString(bodyReceive);
            };

            channel.BasicConsume(
                queue: replyQueue.QueueName,
                autoAck: true,
                consumer: consumer);

            var message = "select * from Log()";
            var body = Encoding.UTF8.GetBytes(message);

            var properties = channel.CreateBasicProperties();
            properties.ReplyTo = replyQueue.QueueName;
            properties.CorrelationId = Guid.NewGuid().ToString();

            channel.BasicPublish("", "logs", properties, body);
        }

        private bool AbrirConexao()
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

            //ao inicializar a conexão, primeiro executa um ping no receptor
            msg = this.brdr.Execute("PING");
            if (msg != null)
            {
                //se a mensagem recebida for a padrão
                if (msg.Equals("OK>"))
                {
                    //define que o receptor está online
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

        private static string VerificaTag(string rfid, string ant)
        {
            string str;
            
            string id = rfid.Substring(rfid.Length - 3);
            int sc = int.Parse(id);

            switch (sc)
            {
                case 081:
                    string lant = VerificaAnt(ant);
                    return $"select * from insert_tag('{rfid}', '708', 'Chicote TE D Celta', '{lant}')";

                case 082:
                    lant = VerificaAnt(ant);
                    return $"select * from insert_tag('{rfid}', '957', 'Parafuso Fixação Motor', '{lant}')";

                case 084:
                    lant = VerificaAnt(ant);
                    return $"select * from insert_tag('{rfid}', '339', 'Vidro Janela Celta', '{lant}')";

                default:
                    return "NOTAG";
            }
        }

        private static string VerificaAnt(string ant)
        {
            int nant = int.Parse(ant);

            switch (nant)
            {
                case 1:
                    return "Pavilhão 1";

                case 2:
                    return "Pavilhão 2";

                case 3:
                    return "Pavilhão 3"; ;

                case 4:
                    return "Pavilhão 4";

                default:
                    return "Não identificado";
            }
        }

        //método que busca os IDs definidos nas tags e imprime na caixa de mensagem
        private void ListaTags()
        {
            string ID = null;
            string ANT = null;
            if (brdr.TagCount > 0)
            {
                //para cada tag identificada
                foreach (Tag tt in brdr.Tags)
                {
                    //lista recebe as tags transformadas em string
                    ID = tt.ToString();

                    //se as tags possuirem alguma informação
                    if (tt.TagFields.ItemCount > 0)
                    {
                        //para cada tag, busca todos os componentes de tagfield (ANT)
                        foreach (TagField tf in tt.TagFields.FieldArray)
                        {
                            ANT = tf.ToString();
                        }
                    }

                    //imprime as tags na caixa de mensagem
                    PostMessageToListBox1("ID: " + ID + ", ANT: " + ANT);

                    string str = VerificaTag(ID, ANT);
                    if(str == "NOTAG")
                    {
                        PostMessageToListBox1("Tag não reconhecida pelo sistema");
                    }
                    //Enviar str para queue Tags para ser adicionada
                    //Enviar str_log para queue Logs para ser adicionada
                    //Atualizar dataGridView1
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
        private void LeituraContinua()
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
            status = brdr.StartReadingTags(null, "ANT", BRIReader.TagReportOptions.POLL);

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

            ListaTags();

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

            status = AbrirConexao();

            if (status == false)
            {
                //não foi possível conectar com o IF2
                MessageBox.Show("Não foi possível conectar");
                button1.Enabled = true;
            }
            else
            {
                PostMessageToListBox1("IF2 conectado!");

                //habilitar botão de leitura
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

            LeituraContinua();
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
