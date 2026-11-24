#!/bin/bash
#
# Does the log server start, and does it take what it is there for?
# Run by ci.yml and nightly.yml on the Debian leg, after a Release build.
#
# The server started with a configuration file of its own, on ports nobody
# else has, and asked:
#
#  * "/" must come back as the embedded web interface - that it is inside the
#    assembly rather than merely to have been built;
#  * the JSON API must answer 401: it is nobody's business until somebody has
#    signed in, and a 200 would mean the door had come off;
#  * the SSH server - the command line, and SFTP - must say its version;
#  * an HTTP upload without credentials must be refused with 401;
#  * signed in as the account of the first start, an uploader is made over the
#    JSON API, and a file it puts by HTTP must come back 201, be listed among
#    the uploads, and be downloaded as it was sent.
#
# Everything it writes goes to a temporary directory: a first start makes an
# account store, a certificate store, a log and the uploads, and none of that
# belongs in a workspace whose cleanliness the build stamps into every assembly.

set -u

tmp="$(mktemp -d)"

cat > "$tmp/configuration.json" <<'JSON'
{ "logServer": { "httpListen": "127.0.0.1" } }
JSON

dotnet LogServerCLI/bin/Release/net11.0/LogServerCLI.dll \
    --port          18363 \
    --ssh-port      22463 \
    --upload-port   18863 \
    --accounts      "$tmp/accounts" \
    --certificates  "$tmp/certificates" \
    --config        "$tmp/configuration.json" \
    --log-file      "$tmp/logs" \
    < /dev/null > "$tmp/console.log" 2>&1 &
pid=$!

web=http://127.0.0.1:18363
put=http://127.0.0.1:18863

# curl -w writes "000" itself when it could not connect; "|| true" keeps its
# exit code from ending the script, and ${:-000} is for no curl at all.
root=000
for _ in $(seq 1 60); do
    root="$(curl -sS -o "$tmp/index.html" -w '%{http_code}' $web/ 2>/dev/null || true)"
    root="${root:-000}"
    [ "$root" != 000 ] && break
    # Gone rather than slow: stop waiting for it.
    kill -0 "$pid" 2>/dev/null || break
    sleep 1
done

api="$(curl -sS -o /dev/null -w '%{http_code}' $web/api/v1/logServer 2>/dev/null || true)"
api="${api:-000}"

# The first line the SSH server says: its version, "SSH-2.0-...".
ssh_banner="$(timeout 5 bash -c 'exec 3<>/dev/tcp/127.0.0.1/22463 && head -n 1 <&3' 2>/dev/null | tr -d '\r' || true)"

printf 'boot\nready\n' > "$tmp/diagnostics.log"

anonymous="$(curl -sS -o /dev/null -w '%{http_code}' -T "$tmp/diagnostics.log" $put/CS001/diagnostics.log 2>/dev/null || true)"
anonymous="${anonymous:-000}"

# The password of the first start, as the banner shows it once.
password="$(sed -n 's/^.*password  *\([^ ]*\)$/\1/p' "$tmp/console.log" | head -n 1)"

login="$(curl -sS -o /dev/null -w '%{http_code}' -c "$tmp/cookies" -H "Origin: $web" \
              --data-urlencode "login=root" --data-urlencode "password=$password" $web/ext/login 2>/dev/null || true)"

made="$(curl -sS -b "$tmp/cookies" -H "Origin: $web" -H 'Content-Type: application/json' \
             -d '{ "id": "CS001" }' $web/api/v1/logServer/uploaders 2>/dev/null || true)"
uploader_password="$(printf '%s' "$made" | sed -n 's/.*"password":"\([^"]*\)".*/\1/p')"

uploaded="$(curl -sS -o "$tmp/uploaded.json" -w '%{http_code}' -u "CS001:$uploader_password" \
                 -T "$tmp/diagnostics.log" $put/CS001/diagnostics.log 2>/dev/null || true)"
uploaded="${uploaded:-000}"

listed="$(curl -sS -b "$tmp/cookies" "$web/api/v1/logServer/uploads?chargingStation=CS001" 2>/dev/null || true)"
id="$(sed -n 's/^{"id":\([0-9]*\),.*/\1/p' "$tmp/uploaded.json" 2>/dev/null)"
curl -sS -b "$tmp/cookies" -o "$tmp/downloaded.log" "$web/api/v1/logServer/uploads/${id:-0}/file" 2>/dev/null || true

kill "$pid" 2>/dev/null || true
wait "$pid" 2>/dev/null || true

echo "GET /                         -> $root"
echo "GET /api/v1/logServer         -> $api"
echo "SSH 22463                     -> ${ssh_banner:-nothing}"
echo "PUT without credentials       -> $anonymous"
echo "Signed in as root             -> ${login:-000}"
echo "Uploader CS001 made           -> $([ -n "$uploader_password" ] && echo yes || echo no)"
echo "PUT as CS001                  -> $uploaded"
echo "Listed of CS001               -> $(printf '%s' "$listed" | grep -o '"total":[0-9]*' || echo nothing)"

# What the banner says this was built from - in every run, not only a red one.
awk '/^  built from/ { f = 1 } f && !/^  built from/ && !/^                 / { f = 0 } f' "$tmp/console.log" || true

fault=0

if [ "$root" != 200 ]; then
    echo "The web interface did not answer."
    fault=1
elif ! grep -q "<title>Log Server</title>" "$tmp/index.html"; then
    echo "Something answered, but it is not the embedded web interface."
    fault=1
fi

[ "$api" = 401 ]       || { echo "The JSON API answered $api where 401 was expected: it is supposed to be shut to anyone not signed in."; fault=1; }

case "$ssh_banner" in
    SSH-2.0-*) ;;
    *) echo "The SSH server did not answer on 22463."; fault=1 ;;
esac

[ "$anonymous" = 401 ] || { echo "An HTTP upload without credentials answered $anonymous where 401 was expected."; fault=1; }
[ "$login" = 201 ]     || { echo "The account of the first start could not sign in."; fault=1; }
[ -n "$uploader_password" ] || { echo "No uploader could be made over the JSON API."; fault=1; }
[ "$uploaded" = 201 ]  || { echo "The uploader's HTTP upload answered $uploaded where 201 was expected."; fault=1; }

printf '%s' "$listed" | grep -q '"total":1' || { echo "The upload is not listed among those of CS001."; fault=1; }
cmp -s "$tmp/diagnostics.log" "$tmp/downloaded.log" || { echo "The file downloaded is not the file uploaded."; fault=1; }

if [ "$fault" != 0 ]; then
    echo
    echo "What it said, without the first-start password:"
    grep -v -i password "$tmp/console.log" || echo "(nothing at all)"
    exit 1
fi
