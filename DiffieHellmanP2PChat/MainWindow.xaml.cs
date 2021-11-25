using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DiffieHellmanP2PChat
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Chat? chat;
        private Task? connectionListenerTask = null;
        private CancellationTokenSource cts = new CancellationTokenSource();


        string ipAddress = "127.0.0.1";
        int port = 8090 + new Random().Next() % 6000;

        public MainWindow()
        {
            InitializeComponent();

            var ct = cts.Token;
            this.chat = new Chat($"{ipAddress}:{port}", ct);
            this.chat.OnNewChatMessage += chat_OnNewChatMessage;
            this.connectionListenerTask = chat.StartAcceptingPeerConnections();

            this.yourAddressLabel.Text = String.Format(this.yourAddressLabel.Text, ipAddress, port.ToString());
        }

        private void chat_OnNewChatMessage(object? sender, NewChatMessageEventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                this.lastChatMessageLabel.Text += Environment.NewLine + e.Message;
                this.scrollContainer.ScrollToBottom();
            });
        }

        private void sendButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.chat == null)
            {
                return;
            }
            string messageText = this.peerAddressText.Text;
            this.peerAddressText.Text = string.Empty;
            if (!chat.CanSendMessage)
            {
                this.chat.ConnectToPeerByAddressAsync(messageText);
            }
            else
            {
                chat.SendMessage(messageText);
            }
        }
    }
}
