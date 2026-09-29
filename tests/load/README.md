# Load and latency campaign (NFR-01, NFR-02, US-26)

The US-26 release gate needs 300 concurrent devices and a 2,000-user load with the NFR-01/NFR-02 thresholds
still holding. These scripts drive that campaign; results must be recorded against the release gate in
[docs/03-release-plan.md](../../docs/03-release-plan.md).

1. Start the server (staging uses PostgreSQL: `docker compose -f deploy/docker-compose.yml up -d`; a laptop run
   can use `dotnet run --project src/Server`).
2. Register and start a 300-module fleet:

   ```sh
   dotnet run --project simulator -- --provision --api https://localhost:7180 --insecure \
       --admin admin@nhatvuong.edu.vn --admin-password Demo@12345 --room A101 --count 300 \
       --prefix LOAD- --credentials fleet.json
   dotnet run --project simulator -- --credentials fleet.json --headless --lan-port 0
   ```

3. Run the API profile (install [k6](https://k6.io)):

   ```sh
   k6 run -e BASE_URL=https://localhost:7180 -e VUS=100 tests/load/api-load.js                    # NFR-02
   k6 run -e BASE_URL=https://localhost:7180 -e VUS=2000 -e DURATION=10m tests/load/api-load.js   # US-26
   ```

4. NFR-01 latency from audit timestamps over the last 100 app commands (administrator token):

   ```sh
   curl -k -H "Authorization: Bearer $TOKEN" https://localhost:7180/api/v1/metrics/command-latency?last=100
   ```

   `withinBudget` is true when P95 ≤ 3,000 ms. The same figure is shown on the app's Admin tab.

Thresholds in `api-load.js` fail the k6 run when NFR-02 (reads P95 < 500 ms) or the NFR-01 command budget is
exceeded, so the exit code can gate a nightly job.
