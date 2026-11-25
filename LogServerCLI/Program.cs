/*
 * Copyright (c) 2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of LogServerCLI <https://github.com/OpenChargingCloud/LogServerCLI>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.CommandLine;
using cloud.charging.open.protocols.WWCP.Node.Configuration;

using cloud.charging.open.LogServer.CommandLine;

#endregion

namespace cloud.charging.open.LogServer
{

    /// <summary>
    /// One log server for charging stations, with a prompt, until 'quit',
    /// Ctrl+C or SIGTERM.
    /// </summary>
    /// <remarks>
    /// What every kind of node's program does is the node's: the switches and
    /// the words -h explains them with, why it could not be set up or could not
    /// start, what goes into the certificate store, the banner and the prompt.
    /// What is left here is the log server's: the switches that say for one
    /// start what the "logServer" section says for every start, and what the
    /// banner says of its uploads.
    /// </remarks>
    public class Program
    {

        #region (private static) Usage

        /// <summary>
        /// What -h shows: every node's switches, in a log server's words, and
        /// the log server's own.
        /// </summary>
        private static readonly NodeUsage Usage = new (

            Program:               "LogServerCLI",
            Kind:                  LogServerNode.LogServerKind,
            DefaultPort:           LogServerNode.DefaultHTTPPort,
            FrontendSources:       "libs/LogServer/LogServer/Frontend",
            ConfigurationSays:     "where the uploads, the name servers and the groups of time servers of this log server live " +
                                  $"(default: {WWCPConfigFile.DefaultFileName} below the repository root). Without the file it takes " +
                                   "uploads by HTTP PUT and over SFTP and keeps them in 'uploads' beside it; the \"logServer\" section " +
                                   "says otherwise - see the README.",
            CertificateKinds:      LogServerNode.CertificateKinds,
            Synopsis:              [ "[--upload-port <n>]", "[--upload-listen <address>]", "[--upload-https]", "[--upload-https-cert <file>]", "[--upload-https-key <file>]",
                                     "[--upload-url <url>]", "[--uploads <directory>]", "[--no-http-uploads | --no-sftp-uploads]", "[--anonymous-uploads]" ],
            AfterTheWebInterface: [
                .. NodeUsage.Wrap("The uploads - each of these says for this start what the \"logServer\" section of the " +
                                  "configuration file says for every start:",
                                  "",
                                  ""),
                .. NodeUsage.Switch("--upload-port <n>",          $"the TCP port HTTP uploads are taken on (default: {LogServerSettings.DefaultHTTPUploadPort}). " +
                                                                  "A port of its own, so that charging stations can reach it while the web interface stays " +
                                                                  "on the loopback."),
                .. NodeUsage.Switch("--upload-listen <address>",  "the address HTTP uploads are taken on (default: \"any\", every address of both " +
                                                                  "families). Not --any, which is the web interface's - and SSH's, and so SFTP's."),
                .. NodeUsage.Switch("--upload-https",             $"take HTTP uploads over TLS, with a certificate of the upload port's own: the certificate store's " +
                                                                  $"TLS server identity for \"{LogServerSettings.UploadListener}\", or one told no listener. Not --https, " +
                                                                  "which is the web interface's."),
                .. NodeUsage.Switch("--upload-https-cert <file>", "the same with the certificate and its intermediates in a PEM file instead - certbot's fullchain.pem."),
                .. NodeUsage.Switch("--upload-https-key <file>",  "the key that goes with it, where that is in a file of its own - certbot's privkey.pem."),
                .. NodeUsage.Switch("--upload-url <url>",         "the URL charging stations reach the upload port at - a name rather than an address, a proxy's " +
                                                                  "\"https\" in front - which the banner and the web interface tell them (default: guessed " +
                                                                  "from --upload-listen, or the name of this machine)."),
                .. NodeUsage.Switch("--uploads <directory>",      $"where the uploads are kept (default: \"{LogServerSettings.DefaultDirectory}\" beside the " +
                                                                  "configuration file)."),
                .. NodeUsage.Switch("--no-http-uploads",          "take no uploads by HTTP PUT."),
                .. NodeUsage.Switch("--no-sftp-uploads",          "take no uploads over SFTP."),
                .. NodeUsage.Switch("--anonymous-uploads",        "take HTTP uploads without credentials as well - from whoever can reach the port."),
                ""
            ]

        );

        #endregion

        #region (private static) WhatToDoAbout(Problem) / SwitchOf(Problem)

        /// <summary>
        /// The way past the upload port, in the words of whoever started it;
        /// null for the web interface's, which is every node's.
        /// </summary>
        private static String? WhatToDoAbout(PortUnavailableException Problem)

            => Problem.Whose == LogServerNode.HTTPUploadPort
                   ? "Stop whatever has it - another log server, a web server - or give HTTP uploads another port with " +
                     "--upload-port <number>, or one address with --upload-listen <address>, or none with --no-http-uploads."
                   : null;

        private static String? SwitchOf(PortUnavailableException Problem)

            => Problem.Whose == LogServerNode.HTTPUploadPort
                   ? "--upload-port"
                   : null;

        #endregion

        #region (private static) BesideTheWebInterface(Node) / OfTheKind(Node)

        /// <summary>
        /// Where uploads are taken, beside the web interface.
        /// </summary>
        private static IEnumerable<(String Label, String Value)> BesideTheWebInterface(LogServerNode Node)
        {
            yield return ("uploads", Node.ListenersText());
        }

        /// <summary>
        /// What the log server keeps, and who may upload.
        /// </summary>
        private static IEnumerable<(String Label, String Value)> OfTheKind(LogServerNode Node)
        {

            yield return ("kept", $"{Node.Uploads.Count} upload(s), {LogServerNode.SizeOf(Node.Uploads.TotalSize)}, in {Node.Uploads.Directory}");

            var uploaders = Node.UploaderAccounts.Count;

            yield return ("uploaders", uploaders == 0
                                           ? $"none yet - '{UploadersCommand.CommandName} add <name>' makes one"
                                           : $"{uploaders}");

        }

        #endregion


        public static async Task<Int32> Main(String[] Arguments)
        {

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            #region Arguments

            // Every node's switches first. What is left is the log server's
            // own, as it was typed.
            var arguments = NodeArguments.Parse(Arguments);

            if (arguments.Refused(Usage) is Int32 refused)
                return refused;

            IPPort?      uploadPort    = null;
            IPAddress?  uploadListen  = null;
            String?      uploads       = null;
            var          noHTTP        = false;
            var          noSFTP        = false;
            var          anonymous     = false;
            var          uploadHTTPS   = false;
            String?      uploadCert    = null;
            String?      uploadKey     = null;
            Uri?         uploadURL     = null;

            var rest = arguments.Rest;

            for (var i = 0; i < rest.Count; i++)
            {
                switch (rest[i])
                {

                    case "--upload-port":
                        if (i + 1 < rest.Count && UInt16.TryParse(rest[i + 1], out var parsedPort) && parsedPort > 0)
                        {
                            uploadPort = IPPort.Parse(parsedPort);
                            i++;
                        }
                        else
                        {
                            NodeProgram.Say(Console.Error, "Missing or invalid port number after --upload-port!");
                            return 2;
                        }
                        break;

                    case "--upload-listen":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out var listenText) ||
                            !LogServerSettings.TryParseListen(listenText, out uploadListen))
                        {
                            NodeProgram.Say(Console.Error, "Missing or invalid address after --upload-listen: an IPv4 or IPv6 address, or \"any\"!");
                            return 2;
                        }
                        break;

                    case "--uploads":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out var directory) || String.IsNullOrWhiteSpace(directory))
                        {
                            NodeProgram.Say(Console.Error, "Missing directory after --uploads!");
                            return 2;
                        }
                        uploads = directory;
                        break;

                    case "--no-http-uploads":
                        noHTTP = true;
                        break;

                    case "--no-sftp-uploads":
                        noSFTP = true;
                        break;

                    case "--anonymous-uploads":
                        anonymous = true;
                        break;

                    case "--upload-https":
                        uploadHTTPS = true;
                        break;

                    case "--upload-https-cert":
                    case "--upload-https-key":
                        var which = rest[i];
                        if (!NodeArguments.TryTakeValue(rest, ref i, out var pemFile) || String.IsNullOrWhiteSpace(pemFile))
                        {
                            NodeProgram.Say(Console.Error, $"Missing PEM file after {which}!");
                            return 2;
                        }
                        if (!File.Exists(pemFile))
                        {
                            NodeProgram.Say(Console.Error, $"{which}: there is no file '{pemFile}'.");
                            return 2;
                        }
                        if (which == "--upload-https-cert")
                            uploadCert = Path.GetFullPath(pemFile);
                        else
                            uploadKey  = Path.GetFullPath(pemFile);
                        break;

                    case "--upload-url":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out var urlText) ||
                            !LogServerSettings.TryParsePublicURL(urlText, out uploadURL, out var urlError))
                        {
                            NodeProgram.Say(Console.Error, $"Missing or invalid URL after --upload-url, as \"https://logs.example.org:8363/\"!");
                            return 2;
                        }
                        break;

                    default:
                        return NodeArguments.Unknown(rest[i], Usage);

                }
            }

            if (uploadHTTPS && uploadCert is not null)
            {
                NodeProgram.Say(Console.Error, "--upload-https shows the certificate store's identity and --upload-https-cert the one in a file: give one of them.");
                return 2;
            }

            if (uploadKey is not null && uploadCert is null)
            {
                NodeProgram.Say(Console.Error, "--upload-https-key is the key of the certificate --upload-https-cert names: give that as well.");
                return 2;
            }

            if (uploadCert is not null && uploadKey is null && !File.ReadAllText(uploadCert).Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                NodeProgram.Say(Console.Error, $"--upload-https-cert: '{uploadCert}' holds no private key. Give the file of its key with --upload-https-key.");
                return 2;
            }

            var uploadTLS = uploadHTTPS        ? new WebTLSConfiguration(WebCertificateSource.Store)
                          : uploadCert is not null ? new WebTLSConfiguration(new WebCertificateSource(uploadCert, uploadKey))
                          : null;

            var root = NodeProgram.RepositoryRoot("LogServerCLI.slnx");

            #endregion

            #region The log server

            LogServerNode node;

            try
            {
                node = new LogServerNode(
                           HTTPHostname:      arguments.HTTPHostname,
                           HTTPPort:          arguments.Port,
                           AccountsPath:      arguments.AccountsPathBelow(root),
                           ConfigFile:        new WWCPConfigFile(arguments.ConfigFilePathBelow(root)),
                           Frontend:          arguments.Frontend,
                           CertificatesPath:  arguments.CertificatesPath,
                           ConsoleLogLevel:   arguments.ConsoleLogLevel,
                           LogPath:           arguments.LogPathBelow(root),
                           BridgeDebugLog:    !arguments.NoTrace,
                           SSH:               arguments.SSH,
                           Web:               arguments.Web,

                           // The switches of this start, over the "logServer"
                           // section: for this start only, never written back.
                           CommandLine:       settings => settings with {
                                                  HTTPPort       = uploadPort   ?? settings.HTTPPort,
                                                  HTTPListen     = uploadListen ?? settings.HTTPListen,
                                                  TLS            = uploadTLS    ?? settings.TLS,
                                                  PublicURL      = uploadURL    ?? settings.PublicURL,
                                                  Directory      = uploads      ?? settings.Directory,
                                                  HTTP           = !noHTTP && settings.HTTP,
                                                  SFTP           = !noSFTP && settings.SFTP,
                                                  AnonymousHTTP  = anonymous || settings.AnonymousHTTP
                                              }
                       );
            }
            catch (Exception e)
            {
                return NodeProgram.CouldNotBeSetUp(LogServerNode.LogServerKind, e, arguments.Verbose);
            }

            await using (node)
            {

                if (node.ImportCertificates(arguments, out _) is Int32 notImported)
                    return notImported;

                if (arguments.ListCertificates)
                    node.ListCertificates();

                if (await node.Started(arguments.Verbose, WhatToDoAbout, SwitchOf: SwitchOf) is Int32 notStarted)
                    return notStarted;

                #region What somebody who just started this needs to know

                foreach (var line in node.Banner(BesideTheInterfaces:  BesideTheWebInterface(node),
                                                 OfTheKind:            OfTheKind(node)))
                    Console.WriteLine(line);

                #endregion

                #region The command line, until 'quit', Ctrl+C or SIGTERM

                // The node's: a prompt where somebody can type, and waiting
                // where nobody can, with the log sharing the screen.
                await new LogServerCLI(node).RunUntilStopped();

                #endregion

            }

            #endregion

            return 0;

        }

    }

}
