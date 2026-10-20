import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import urllib.request

root = Path(__file__).resolve().parents[1]
dotnet = shutil.which("dotnet")
if dotnet is None:
    raise SystemExit("Install the SDK selected by global.json and add dotnet to PATH.")
subprocess.run([dotnet, "build", "Immunization.sln", "-c", "Release", "--no-restore"], cwd=root, check=True)
parser = argparse.ArgumentParser()
parser.add_argument("--input", type=Path)
args = parser.parse_args()
with tempfile.TemporaryDirectory(prefix="immunization-demo-") as directory:
    input_directory = args.input.resolve() if args.input else Path(directory) / "input"
    if not args.input:
        input_directory.mkdir()
        fixture = (root / "demo/fixture.hl7").read_bytes()
        for index in range(8):
            (input_directory / f"message-{index + 1:02}.hl7").write_bytes(fixture)
    files = sorted(input_directory.glob("*.hl7"))
    if len(files) != 8:
        raise RuntimeError("This demonstration expects eight input files.")
    before = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
    log = Path(directory) / "server.log"
    with log.open("w") as output:
        demo_credentials = json.loads((root / "demo/Immunization.Demo/appsettings.json").read_text())["Demo"]["Credentials"]
        environment = os.environ | {
            "REGISTRY_CLINIC_OID": demo_credentials["ClinicOid"],
            "REGISTRY_TRANSFER_USER": demo_credentials["User"],
            "REGISTRY_TRANSFER_PASSWORD": demo_credentials["Password"]
        }
        server = subprocess.Popen([dotnet, str(root / "demo/Immunization.Demo/bin/Release/net8.0/Immunization.Demo.dll")],
                                  cwd=root / "demo/Immunization.Demo", stdout=output, stderr=subprocess.STDOUT)
        try:
            for attempt in range(100):
                if server.poll() is not None:
                    raise RuntimeError("Demo server exited. " + log.read_text())
                try:
                    probe = urllib.request.Request("http://127.0.0.1:5080/echo", data=json.dumps({"user": demo_credentials["User"], "password": demo_credentials["Password"]}).encode(), headers={"Content-Type": "application/json"})
                    with urllib.request.urlopen(probe, timeout=0.5) as response:
                        if json.loads(response.read()) == {"echoBack": "echo"}:
                            break
                except OSError:
                    time.sleep(0.1)
            else:
                raise RuntimeError("Demo server did not become ready.")
            report_dir = root / "reports"
            report_dir.mkdir(exist_ok=True)
            report = report_dir / ("demo-" + str(time.time_ns()) + ".jsonl")
            result = subprocess.run([dotnet, str(root / "src/Immunization.Cli/bin/Release/net8.0/Immunization.Cli.dll"),
                                     "run", "config/demo.json", str(input_directory), str(report)], cwd=root, check=False, env=environment)
            if result.returncode != 2:
                raise RuntimeError(f"Expected partial/unknown exit code 2, received {result.returncode}.")
            entries = [json.loads(line) for line in report.read_text().splitlines()]
            completed = [entry["Receipt"] for entry in entries if entry["State"] == "Completed"]
            outcomes = [entry["Assessment"]["Outcome"] for entry in completed]
            expected = ["Accepted", "Observed", "Rejected", "Unknown", "Unknown", "Unknown", "Unknown", "Accepted"]
            if outcomes != expected or len(entries) != len(files) * 2:
                raise RuntimeError(f"Unexpected transaction report: {outcomes}")
            if len({entry["Id"] for entry in completed}) != len(files):
                raise RuntimeError("Transaction identifiers are not unique.")
            if before != {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in files}:
                raise RuntimeError("Input files changed.")
            print("DEMO VERIFIED: eight simulated responses, correlated report, unchanged input bytes.")
        finally:
            server.terminate()
            try:
                server.wait(timeout=5)
            except subprocess.TimeoutExpired:
                server.kill()
                server.wait()
