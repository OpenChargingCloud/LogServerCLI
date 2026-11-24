# LogServerCLI

One log server for charging stations: it takes the log files they upload - by
HTTP PUT on a port of its own, and over SFTP on its SSH server - keeps each of
them with what is known about how it came, and shows them in a web interface,
a JSON API and a prompt - at the console and over SSH - until 'quit' or Ctrl+C.

What a charging station is given as the location of OCPP's GetLog or
GetDiagnostics is an upload URL of this server, with the credentials of its
account in it; whatever arrives is listed at once - by time, filtered by
uploader and by charging station - to be read, downloaded or removed.

It is a sibling of [DNSServerCLI](https://github.com/OpenChargingCloud/DNSServerCLI),
[NTSServerCLI](https://github.com/OpenChargingCloud/NTSServerCLI),
[PKICLI](https://github.com/OpenChargingCloud/PKICLI) and the others, and is
built the same way. The HTTP and SSH servers below it are
[Hermod](https://github.com/Vanaheimr/Hermod)'s; what every program of the
family has - the sign-in, the name servers it asks itself, the groups of time
servers and the certificates they are held to, the SSH server with its command
line and its SFTP tree, and the log - is
[WWCP_Node](https://github.com/OpenChargingCloud/WWCP_Node); what is the log
server's own is in [LogServer](https://github.com/OpenChargingCloud/LogServer).


### Getting it

The libraries it is built from are submodules, so they have to come along:

```
git clone --recurse-submodules https://github.com/OpenChargingCloud/LogServerCLI.git
```

If you already cloned it without them:

```
git submodule update --init --recursive
```

**On Windows**, turn long paths on first: `git config --global core.longpaths true`.


### Building and running it

.NET 11 and Node 22 or newer. The build runs `npm ci` and `npm run build` for
the web interface itself whenever its inputs changed, and embeds it into the
assembly; `-p:SkipFrontendBuild=true` builds the rest alone.

```
dotnet build LogServerCLI.slnx
dotnet run --project LogServerCLI
```

or `./run.sh`, which takes the same switches. `--help` lists all of them.
Without a configuration file it takes uploads by HTTP PUT on port 8363, on
every address of both families, and over SFTP on its SSH server, and keeps
them in `uploads/` beside the configuration file - and serves its web
interface, its JSON API and its command line over SSH on 127.0.0.1 only:
http://127.0.0.1:2363/ and port 22363 (`--any` for every address, which SFTP
then listens on as well). At the first start it makes one account, `root`, and
shows its password once, on the console.

Every switch of the log server says for one start what the `logServer` section
says for every start:

| switch | |
|---|---|
| `--upload-port <n>` | the TCP port HTTP uploads are taken on (8363) |
| `--upload-listen <address>` | where HTTP uploads are taken (`any`, every address of both families) |
| `--uploads <directory>` | where the uploads are kept (`uploads` beside the configuration file) |
| `--no-http-uploads` | no uploads by HTTP PUT |
| `--no-sftp-uploads` | no uploads over SFTP |
| `--anonymous-uploads` | HTTP uploads without credentials as well |

A start that cannot have its port ends there and says which switch to give
instead.


### Uploaders

Files are uploaded under accounts of the node in the `uploader` role - one a
charging station, as a rule, named as the station is. They are made on the
**Uploaders** page, or at the prompt:

```
uploaders add CS001 The charger in the yard
```

which answers with the password of the new account, shown this once.
`uploaders password CS001` makes it a new one, `uploaders remove CS001` takes
the role away again; what it uploaded is kept.


### Uploading by HTTP PUT

```
PUT http://<host>:8363/<charging station>/<file name>
PUT http://<host>:8363/<file name>
```

The body is the file, as it is. Signed in with HTTP Basic Auth as an uploader;
where the path names no charging station, the station is the uploader. A back
end that uploads in the name of several stations names each in the path.

What a charging station is given as the location of GetLog or GetDiagnostics:

```
http://CS001:<password>@logs.example.org:8363/CS001/
```

and to try it by hand:

```
curl -u CS001:<password> -T diagnostics.zip http://127.0.0.1:8363/CS001/diagnostics.zip
```

It answers `201 Created` with the upload's metadata; `401` without
credentials (or with wrong ones), `403` for an account that is no uploader,
`413` for a file larger than `maxFileSize`. Nothing else is served on that
port.

Over plain HTTP the file and the password cross the network in the clear, and
the log server says so at every start. Where the web interface speaks TLS -
`"web": { "tls": ... }`, or `--https` - the upload port shows the same
certificate; otherwise put a proxy that speaks TLS in front.


### Uploading over SFTP

The node's SSH server serves an SFTP tree, and the log server adds `/upload`
to it. An uploader signs in with one of its SSH keys - given to it with
`sshKeys` or `--authorize-ssh-key CS001=<file>` - or, where the `ssh` section
says `"passwords": true`, with its password, and puts its files there:

```
sftp -P 22363 CS001@logs.example.org
sftp> put diagnostics.log /upload/
```

Every file put in `/upload` is taken into the store at once, so that the next
one of the same name never takes its place. The charging station of an SFTP
upload is the uploader it signed in as.


### The configuration

`configuration.json` below the repository root, or what `--config` names. The
section of the log server, with every key and its default:

```json
{
  "logServer": {
    "directory":      "uploads",
    "http":           true,
    "httpListen":     "any",
    "httpPort":       8363,
    "anonymousHTTP":  false,
    "sftp":           true,
    "maxFileSize":    104857600
  },
  "ssh": {
    "passwords":      true
  }
}
```

SFTP is served by default on a log server; `"ssh": { "sftp": false }` switches
it off for the whole node.


### Where the uploads are kept

Each file in a directory of its charging station below `directory`, under a
name of its own making - when it came, its number, and the name it came as -
so that nothing ever overwrites anything; and one line about it in
`uploads.jsonl` beside them: its number, time, protocol, uploader, charging
station, file name, size, SHA-256, the address it came from, and for HTTP the
Content-Type and User-Agent it said.


### The prompt

Beside every node's commands:

* `logServer` - where it takes uploads, how many it keeps, the newest;
* `uploads [<charging station>]` - the newest uploads, of every station or of
  one; `uploads remove <number>` takes one away, its file with it;
* `uploaders [add <name> [<what it is called>] | password <name> | remove <name>]`.

The prompt is the console's, and every account's over SSH:
`ssh -p 22363 root@127.0.0.1`.


### The web interface

http://127.0.0.1:2363/, signed in with an account of the node. Beside what
every node shows - its configuration, the name and time servers it asks
itself, the SSH server, the certificate store and the log - the log server has
three pages of its own:

* **Uploads** - every upload: when, of which charging station, the file, its
  size, HTTP or SFTP, by which uploader, from where; sorted by time, newest or
  oldest first, filtered by charging station and by uploader (kept in the
  address of the page, so that it can be sent as a link); a row opened shows
  its SHA-256, where it is kept, its Content-Type and User-Agent. Each to view
  as text, to download, and to remove. A new upload appears as it arrives.
* **Log server** - how uploads are taken, the URL a charging station is
  given, the `curl` and `sftp` lines to try it with, and what is kept.
* **Uploaders** - the accounts in the uploader role, how many uploads each
  made and when last; made with a form, given a new password, taken away.

The frontend is TypeScript and SCSS, drawn with lit-html and bundled by
webpack, in `libs/LogServer/LogServer/Frontend`; what every kind of node
shares comes from `libs/WWCP_Node/Frontend`.


### The JSON API

Below http://127.0.0.1:2363/api/, beside every node's routes, for an account
signed in at `/ext/login`:

| | |
|---|---|
| `GET  v1/logServer` | how the log server stands |
| `GET  v1/logServer/settings` | the `logServer` section as it is in effect |
| `GET  v1/logServer/uploads` | the uploads - `?uploader=`, `?chargingStation=`, `?since=`, `?until=`, `?order=newest\|oldest`, `?skip=`, `?take=` |
| `GET  v1/logServer/uploads/{id}` | one upload |
| `GET  v1/logServer/uploads/{id}/file` | its file; `?inline=true` as text |
| `POST v1/logServer/uploads/{id}/remove` | the upload and its file taken away |
| `GET  v1/logServer/uploaders` | the uploaders |
| `POST v1/logServer/uploaders` | `{ "id", "name"? }` - an uploader more, answered with its password |
| `POST v1/logServer/uploaders/password` | `{ "id" }` - a new password |
| `POST v1/logServer/uploaders/remove` | `{ "id" }` - no longer an uploader |

The event stream `v1/events` says `upload` and `uploadRemoved` as they happen.


### Who may do what

Three resources beside the node's: `uploads` - read, listed and downloaded;
edited, removed - `uploaders` - read, listed; edited, made, given a new
password, taken away - and `upload` - run, a file put. Two roles beside the
node's viewer and administrators: `uploader`, which may put files and nothing
else, and `operator`, who reads everything, downloads and removes uploads and
asks the name and time servers, but makes no uploaders.


### Where things are

| | |
|---|---|
| `LogServerCLI/` | the command line: the log server's own switches, and its lines of what the console says at a start |
| `libs/LogServer/` | the log server itself - its settings, the store of the uploads, the upload port, the SFTP area, the uploaders, its JSON API, what can be typed at it - and its tests |
| `libs/LogServer/LogServer/Frontend/` | the web interface: TypeScript and SCSS, bundled by webpack |
| `libs/WWCP_Node/` | what every one of these programs shares |
| `libs/WWCP_Core/` | of WWCP Core only WWCP_CoreData, which WWCP_Node builds on |
| `libs/Hermod/` | the network below all of them: HTTP, TLS, SSH, SFTP and DNS |
| `libs/Norn/` | NTS and NTP, with which the node checks its clock |
| `libs/Styx/`, `libs/Urdr/` | the basics below that, and time stamps (RFC 3161) |


### Its tests

```
dotnet test libs/LogServer/LogServerTests/LogServerTests.csproj
```

The store on its own - where a file is kept, the index read back to the
millisecond, filtered and ordered, an upload removed - and a log server
started on the loopback address: an uploader made over the JSON API uploads by
HTTP PUT, its uploads listed, filtered and downloaded; an upload without
credentials or by an account that is no uploader refused; an operator may read
but not make uploaders; a new password and a removed uploader shut the old
credentials out - and WWCP_Node's conformance suite asked of a log server, its
web interface among it. The web interface's own tests are run by node, in
`libs/LogServer/LogServer/Frontend`:

```
npm run typecheck
npm run typecheck:test
npm test
```


## Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
