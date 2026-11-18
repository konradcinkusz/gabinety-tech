# Image of the daily backup machine (T-10.05). The pg_dump client MAJOR must equal the server's (postgres:17-alpine in postgres.fly.toml).
# Build context: deploy/fly/backup. Not built in CI, not built or run on Fly yet (see README.md, "Not verified").
FROM postgres:17-alpine
RUN apk add --no-cache bash coreutils rclone
COPY backup.sh retention.sh restore-check.sh run-daily.sh /opt/backup/
RUN chmod 0555 /opt/backup/*.sh
USER postgres
ENTRYPOINT ["/opt/backup/run-daily.sh"]
