using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Intermec.DataCollection.RFID;
using Newtonsoft.Json;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static Npgsql.Replication.PgOutput.Messages.RelationMessage;
using System.IO;

namespace RFID
{
    public partial class Form1 : Form
    {
        //Inicialização de variáveis para o RFID
        public BRIReader brdr = null;
        private bool bReaderOffline = true;
        private string[] MyTagList = new string[100];

        //Inicializa o Form
        public Form1()
        {
            InitializeComponent();
        }

        //#### RabbitMQ ####

        //Esse método é utilizado para comandos SQL que não recebem uma resposta
        //Como as duas chamadas para inserir dados nas tabelas
        private static void RMQ_Send(string sql, string queue)
        {
            //Nomeia o host
            var factory = new ConnectionFactory()
            {
                HostName = "localhost"
            };

            //Cria uma nova conexão com o nome do host
            //E um novo modelo de canal com base na conexão
            var connection = factory.CreateConnection();
            var channel = connection.CreateModel();

            //Declara o canal que será utilizado para enviar as mensagens
            //O canal escolhido é passado como parâmetro na chamada do método
            channel.QueueDeclare(queue: queue,
                                 durable: false,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

            //A mensagem enviada é o SQL completo
            string messageSend = sql;

            //Codificação da mensagem em bytes para ser enviada
            var bodySend = Encoding.UTF8.GetBytes(messageSend);

            //Ação de publicar a mensagem no canal
            channel.BasicPublish(exchange: "",
                                 routingKey: queue,
                                 basicProperties: null,
                                 body: bodySend);
        }

        //Esse método realiza o envido da mensagem pelo broker no canal escoliho
        //E possui uma fila de resposta para essa mensagem, a replyQueue
        //Essa resposta é tratada e mostrada em um novo Forms
        private void RMQ_RequestReply(string sql, string queue)
        {
            //Nomeia o host
            var factory = new ConnectionFactory()
            {
                HostName = "localhost"
            };

            //Cria uma nova conexão com o nome do host
            //E um novo modelo de canal com base na conexão
            var connection = factory.CreateConnection();
            var channel = connection.CreateModel();

            //Declara a fila de resposta da mensagem
            var replyQueue = channel.QueueDeclare(
                queue: "",
                exclusive: true);

            //Declara o canal que será utilizado para enviar as mensagens
            //O canal escolhido é passado como parâmetro na chamada do método
            channel.QueueDeclare(
                queue,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            //Consome a mensagem do canal
            var consumer = new EventingBasicConsumer(channel);

            //Recebimento da mensagem de resposta
            consumer.Received += (model, ea) =>
            {
                //Com a mensagem recebida, converte-se ela para string
                var bodyReceive = ea.Body.ToArray();
                var messageReceive = Encoding.UTF8.GetString(bodyReceive);

                //Envia a resposta como parâmetro para a chamada de um novo Form
                //Esse Form mostra a resposta em um dataGrid
                F_Window janela = new F_Window(messageReceive, queue);
                janela.ShowDialog();
            };

            //Consome a mensagem vinda do canal de resposta
            channel.BasicConsume(
                queue: replyQueue.QueueName,
                autoAck: true,
                consumer: consumer);

            //Envio da mensagem SQL
            //A mensagem enviada é o SQL completo
            var message = sql;

            //Codificação da mensagem em bytes para ser enviada
            var body = Encoding.UTF8.GetBytes(message);

            //Define as propriedades do canal
            //Declarando o canal de resposta para a mensagem
            var properties = channel.CreateBasicProperties();
            properties.ReplyTo = replyQueue.QueueName;
            properties.CorrelationId = Guid.NewGuid().ToString();

            //Publica a mensagem SQL
            channel.BasicPublish("", queue, properties, body);
        }

        //#### IF2 ####

        //Abre uma conexão com o IF2
        private bool AbrirConexao()
        {
            //Inicializa variáveis necessárias
            bool status = false;
            string msg = null;
            string connect = null;

            //Verifica qual a forma de conexão selecionada
            if (radioButton1.Checked == true)
            {
                //Conexão serial
                connect = textBox3.Text;
            }
            else
            {
                //Conexão por TCP/IP
                connect = textBox4.Text;
            }

            try
            {
                //Cria uma nova conexão utilizando o endereço dado
                brdr = new BRIReader(this, connect);
                status = true;
            }
            //Se não foi possível criar a conexão, mostra um erro
            catch (BasicReaderException ex)
            {
                MessageBox.Show(ex.ToString());
                status = false;
            }

            //Se não foi possível criar uma conexão OU o status permaneceu falso
            //A conexão falhou
            if (brdr == null || status == false)
            {
                bReaderOffline = true;

                //Utiliza a caixa de texto para informar todas as operações com o IF2
                PostarMensagem("Não foi possível conectar ao leitor!");
                PostarMensagem("Endereço: " + connect);
                return false;
            }

            //Ao iniciar a conexão, realiza um PING com o leitor
            msg = this.brdr.Execute("PING");
            if (msg != null)
            {
                //Se a mensagem recebida for a padrão
                if (msg.Equals("OK>") || msg.Equals("PING\r\nOK>"))
                {
                    PostarMensagem("Leitor online");
                    //Define que o leitor está online
                    bReaderOffline = false;
                    status = true;
                }
            }

            //Se o status for falso após todo o processo, não foi possível conectar ao leitor
            if (status == false)
            {
                //Publica na caixa de mensagem que não foi possível conectar
                PostarMensagem("Não foi possível conectar ao leitor!");
                PostarMensagem("Endereço: " + connect);
                bReaderOffline = true;
            }
            return status;
        }

        //O método de leitura adotado é o que trata as Tags como Eventos
        //Portanto, é necessário um Handler para esse tipo de leitura
        void brdr_EventHandlerTag(object sender, EVTADV_Tag_EventArgs EvtArgs)
        {
            //Todos os dados lidos quando uma Tag é detectada são passados para essa variável
            string sTagData = EvtArgs.DataString.ToString();

            //Considerando o separado definido no BRIServer
            //É feito um Split para recuperar todas as informações
            //Esses dados são colocados em uma Array de strings
            string[] Tag = sTagData.Split(' ');

            //Imprime a Tag lida e por qual antena ela foi identificada na caixa de mensagem
            PostarMensagem("ID: " + Tag[0] + ", ANT: " + Tag[1]);

            //Tratamento realizado utilizando a Tag e a antena
            //Esse tratamento é feito para a inserção no banco de dados
            string[] str = VerificaTag(Tag[0], Tag[1]);

            //Caso a Tag não seja identificada pelo sistema, ela não é lida
            if (str[0] == "NOTAG")
            {
                PostarMensagem("Tag não reconhecida pelo sistema!");
                return;
            }

            //Envia a mensagem de inserção da tag para o broker
            //A fila escolhida é especifica para essa ação
            RMQ_Send(str[0], "Itag");

            //Envia a mensagem de inserção do log para o broker
            RMQ_Send(str[1], "Ilog");
        }

        //Leitura das Tags e tratamento delas como Eventos
        private void LerTagComoEvento()
        {
            //A leitura dessas Tags é contínua
            //Cada Tag só pode ser identificada uma vez a cada vez que a leitura é aberta
            bool status = false;

            //Verifica se a conexão foi feita com sucesso
            if (bReaderOffline == true)
            {
                PostarMensagem("Não foi possível conectar ao leitor!");
                return;
            }

            //Realiza a leitura com a opção de Evento
            //Para saber por qual antena a Tag foi passada, passa-se o parâmetro "ANT"
            status = brdr.StartReadingTags(null, "ANT", BRIReader.TagReportOptions.EVENT);
        }

        //Código para escrever um valor no campo de ID
        //Normalmente, essa ação não seria realizada no mesmo terminal que realiza a leitura
        //Mas esse método será incluído para apresentar as possibilidades do leitor
        private void EscreveTag()
        {
            string wResp = null;

            //Verifica se a conexão foi feita com sucesso
            if (bReaderOffline == true)
            {
                PostarMensagem("Não foi possível conectar ao leitor!");
                return;
            }


            wResp = this.brdr.Execute($"W EPCID={textBox2.Text}");

            int x = 0;
            string delimStr = null;
            string[] tList = null;
            char[] delimiter = null;
            int RspCount = 0;

            delimStr = "\n";
            delimiter = delimStr.ToCharArray();
            tList = wResp.Replace("\r\n", "\n").Split(delimiter);
            RspCount = tList.Length;
            for (x = 0; x < RspCount; x++)
            {
                PostarMensagem(tList[x]);
            }

            //you need to check the response to determine if the write was successful
            if (wResp.IndexOf("WROK") > 0)
            {
                PostarMensagem("Tag escrita com sucesso!");
                PostarMensagem($"Tag: {textBox2.Text}");
            }
            else
            {
                PostarMensagem("Erro ao escrever na Tag!");
                PostarMensagem($"Tag: {textBox2.Text}");
            }
        }


        //#### Tratamento ####
        //Faz a verificação da Tag e qual é sua correspondência no sistema
        private static string[] VerificaTag(string rfid, string ant)
        {
            string[] sql;
            sql = new string[2];

            //O identificador para o produto na Tag foi classificado como os 3 últimos caracteres
            //Todas as Tags usadas possuem o padrão "0xx", sendo xx um número identificado do produto
            string id = rfid.Substring(rfid.Length - 3);

            //String é transformada em int para realizar o switch case
            int sc = int.Parse(id);

            //Switch case trata cada RFid da forma seguindo o contexto do problema ser uma empresa de automóveis
            switch (sc)
            {
                //Para cada case, retorna a string SQL para inserir na tabela Tags e na tabela Logs
                case 081:
                    string lant = VerificaAnt(ant);
                    sql[0] = $"select * from insert_tag('{rfid}', '708', 'Chicote TE D Celta', '{lant}')";
                    sql[1] = $"select * from insert_log('{rfid}', '708', 'Chicote TE D Celta', '{lant}', '{DateTime.Now}')";
                    return sql;

                case 082:
                    lant = VerificaAnt(ant);
                    sql[0] = $"select * from insert_tag('{rfid}', '957', 'Parafuso Fixação Motor', '{lant}')";
                    sql[1] = $"select * from insert_log('{rfid}', '957', 'Parafuso Fixação Motor', '{lant}', '{DateTime.Now}')";
                    return sql;

                case 084:
                    lant = VerificaAnt(ant);
                    sql[0] = $"select * from insert_tag('{rfid}', '339', 'Vidro Janela Celta', '{lant}')";
                    sql[1] = $"select * from insert_log('{rfid}', '339', 'Vidro Janela Celta', '{lant}', '{DateTime.Now}')";
                    return sql;
                
                //Caso a Tag não for identificada no sistema (interferência), é dado uma mensagem padrão
                default:
                    sql[0] = sql[1] = "NOTAG";
                    return sql;
            }
        }

        //Método para retornar o local da antena com base no número dela
        private static string VerificaAnt(string ant)
        {
            int nant = int.Parse(ant);

            switch (nant)
            {
                case 1: return "Pavilhão 1";
                case 2: return "Pavilhão 2";
                case 3: return "Pavilhão 3";
                case 4: return "Pavilhão 4";
                default: return "Não identificado";
            }
        }

        //Método para postar as mensagens na caixa de mensagem
        private void PostarMensagem(string sMsg)
        {
            listBox1.Items.Add(sMsg);
            listBox1.SelectedIndex = listBox1.Items.Count - 1;
            listBox1.Refresh();
        }


        //#### Botões ####
        
        //Botão de conexão com o IF2
        private void button1_Click(object sender, EventArgs e)
        {
            bool status = true;
            button1.Enabled = false;

            //Abre uma conexão e verifica o status retornado
            status = AbrirConexao();

            if (status == false)
            {
                //não foi possível conectar com o IF2
                PostarMensagem("Não foi possível conectar ao leitor!");
                button1.Enabled = true;
            }
            else
            {
                PostarMensagem("IF2 conectado!");

                //Adiciona o Handler de Evento na leitura da Tag
                brdr.EventHandlerTag += new Tag_EventHandlerAdv(brdr_EventHandlerTag);

                //Habilitar botão de leitura
                button2.Enabled = true;
            }
        }

        //Botão para iniciar a leitura
        private void button2_Click(object sender, EventArgs e)
        {
            if (bReaderOffline == true)
            {
                PostarMensagem("Leitor está offline");
                return;
            }

            //Inicia leitura das Tags
            LerTagComoEvento();

            //Desabilita esse botão e habilita o botão de parar leitura
            button2.Enabled = false;
            button3.Enabled = true;
        }

        //Botão para finalizar leitura
        private void button3_Click(object sender, EventArgs e)
        {
            //Para de realizar a leitura
            brdr.StopReadingTags();

            //Desabilita esse botão e habilita o botão de leitura
            button2.Enabled = true;
            button3.Enabled = false;
        }

        //Botão para buscar as Tags com o PN (Part Number) informado
        private void button4_Click(object sender, EventArgs e)
        {
            string sql = $"SELECT * FROM find_tags('{textBox1.Text}')";
            RMQ_RequestReply(sql, "RRtag");
        }

        private void button5_Click(object sender, EventArgs e)
        {
            string sql = "SELECT * FROM log()";
            RMQ_RequestReply(sql, "RRlog");
        }

        private void button6_Click(object sender, EventArgs e)
        {
            EscreveTag();
        }
    }
}
