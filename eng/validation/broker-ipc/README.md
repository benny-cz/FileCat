# Owned broker read-server identity control

`Probe.csproj` builds a requester named FileCat.exe against published FileCat assemblies named by `BrokerAssemblyRoot`. This is a disposable Windows component control, requiring an administrative test account and a fresh owned Program Files fixture beside an unchanged production helper. It is not a normal FileCat launch or an installed-candidate consent test.

The probe independently checks that PhysicalDrive999 is absent before creating a plan for that deliberately nonexistent device. It calls the actual `BrokeredDeviceSource.Open`; a synthetic server in the same requester process reads the owned plan's nonce and binds that pipe. The server can return only bytes from a new owned 32 KiB regular file. Before/after direct pipe positives check exact bytes. Counterfeit protocol request/reply byte counts distinguish refusal before Info from acceptance. No physical device is opened and no authorization dialog is operated.

Pass fresh nonexistent absolute paths for the owned case directory and JSON output. Preserve source/published/helper input hashes and verify them independently. On completion the probe stops only helper processes whose image is the exact helper in this unique fixture and removes only its own claimed nonce value. The surrounding runner must independently verify output/source immutability and process/protected-fixture cleanup. Run with a bounded supervisor and retain failures. This fixture deliberately uses an already-administrative caller; limited-caller UAC, complete token/path authentication and actual consent remain separate release tests.

Example build: `dotnet publish Probe.csproj -c Release -r win-x64 --self-contained true -p:BrokerAssemblyRoot=<exact-published-helper-directory> -o <fresh-probe-output>`.
