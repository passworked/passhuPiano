"""Polite, resumable classical MIDI collection with a fixed eight-hour deadline.

Python standard library only. Downloaded assets remain outside Unity's Assets.
"""
from __future__ import annotations

import argparse
import concurrent.futures
import csv
import datetime as dt
import email.utils
import hashlib
import io
import json
import logging
import os
from pathlib import Path
import re
import sqlite3
import struct
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import urllib.robotparser
import zipfile

import sources

TZ = dt.timezone(dt.timedelta(hours=8))
USER_AGENT = "ClassicalMidiCollector/1.0 (personal MIDI collection; 1 request/sec/host)"


def timestamp(value=None):
    return dt.datetime.fromtimestamp(value or time.time(), TZ).isoformat(timespec="seconds")


def atomic_json(path, data):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(temporary, path)


def safe_name(text, length=105):
    text = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "_", str(text))
    text = re.sub(r"\s+", " ", text).strip(" ._")[:length].rstrip(" .") or "untitled"
    if text.split(".")[0].upper() in {"CON", "PRN", "AUX", "NUL", *(f"COM{i}" for i in range(1, 10)), *(f"LPT{i}" for i in range(1, 10))}:
        text = "_" + text
    return text


def validate_midi(raw):
    """Validate SMF chunks and event boundaries; distinguish Unity compatibility."""
    if len(raw) < 14 or raw[:4] != b"MThd":
        raise ValueError("Response is not a Standard MIDI File")
    header = struct.unpack_from(">I", raw, 4)[0]
    fmt, tracks, division = struct.unpack_from(">HHH", raw, 8)
    if header < 6 or fmt not in (0, 1, 2) or not tracks or not division:
        raise ValueError("Invalid MIDI header")
    if fmt == 0 and tracks != 1:
        raise ValueError("SMF format 0 must have exactly one track")
    offset, notes = 8 + header, 0

    def vlq(pos, end):
        value = 0
        for _ in range(4):
            if pos >= end:
                raise ValueError("Truncated variable-length quantity")
            byte = raw[pos]
            pos += 1
            value = (value << 7) | (byte & 127)
            if not byte & 128:
                return value, pos
        raise ValueError("Invalid variable-length quantity")

    for _ in range(tracks):
        if raw[offset:offset + 4] != b"MTrk" or offset + 8 > len(raw):
            raise ValueError("Missing MIDI track")
        size = struct.unpack_from(">I", raw, offset + 4)[0]
        pos, end, running = offset + 8, offset + 8 + size, None
        has_end = False
        if end > len(raw):
            raise ValueError("Truncated MIDI track")
        while pos < end:
            _, pos = vlq(pos, end)
            if pos >= end:
                raise ValueError("Truncated MIDI event")
            status = raw[pos]
            if status & 128:
                pos += 1
                if status < 0xF0:
                    running = status
            elif running is not None:
                status = running
            else:
                raise ValueError("Running status without channel status")
            if status == 0xFF:
                if pos >= end:
                    raise ValueError("Truncated meta event")
                meta_type = raw[pos]
                pos += 1
                length, pos = vlq(pos, end)
                pos += length
                if meta_type == 0x2F:
                    if length != 0 or pos != end:
                        raise ValueError("Invalid End-of-Track event")
                    has_end = True
            elif status in (0xF0, 0xF7):
                length, pos = vlq(pos, end)
                pos += length
                running = None
            elif 0x80 <= status <= 0xEF:
                length = 1 if status & 0xF0 in (0xC0, 0xD0) else 2
                if pos + length > end or any(b & 128 for b in raw[pos:pos + length]):
                    raise ValueError("Invalid channel event")
                if status & 0xF0 == 0x90 and raw[pos + 1] > 0:
                    notes += 1
                pos += length
            else:
                raise ValueError(f"Unsupported system event {status:#x} in SMF")
            if pos > end:
                raise ValueError("Event exceeds MIDI track")
        if not has_end:
            raise ValueError("Missing End-of-Track event")
        offset = end
    if offset != len(raw) or notes == 0:
        raise ValueError("Trailing data or no note-on events")
    return {"smf_format": fmt, "tracks": tracks, "division": division,
            "note_on_events": notes, "unity_compatible": fmt in (0, 1) and not division & 0x8000}


class Stopped(Exception):
    pass


class Forbidden(Exception):
    pass


class Network:
    def __init__(self, root, stop_at):
        self.root, self.stop_at = root, stop_at
        self.lock = threading.Lock()
        self.hosts = {}
        self.transferred = 0
        self.stop_event = threading.Event()

    def check(self):
        if self.stop_event.is_set() or time.time() >= self.stop_at or (self.root / "STOP").exists():
            raise Stopped()

    def host(self, url):
        split = urllib.parse.urlsplit(url)
        origin = f"{split.scheme}://{split.netloc}"
        with self.lock:
            return origin, self.hosts.setdefault(origin, {"lock": threading.Lock(), "last": 0, "robots": None, "delay": 1.1})

    def open(self, url, headers=None):
        self.check()
        req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, **(headers or {})})
        return urllib.request.urlopen(req, timeout=max(0.1, min(25, self.stop_at - time.time())))

    def fetch(self, url, destination=None):
        origin, host = self.host(url)
        with host["lock"]:
            self.check()
            if host["robots"] is None:
                robot_url = origin + "/robots.txt"
                try:
                    with self.open(robot_url) as response:
                        robots = response.read().decode("utf-8", "replace")
                except urllib.error.HTTPError as exc:
                    if exc.code not in (404, 410):
                        raise
                    robots = "User-agent: *\nDisallow:\n"
                parser = urllib.robotparser.RobotFileParser(robot_url)
                parser.parse(robots.splitlines())
                host["robots"] = parser
                host["delay"] = max(1.1, parser.crawl_delay(USER_AGENT) or parser.crawl_delay("*") or 0)
                name = safe_name(urllib.parse.urlsplit(url).netloc)
                (self.root / "evidence" / f"{name}-robots.txt").write_text(robots, encoding="utf-8")
                host["last"] = time.monotonic()
            if not host["robots"].can_fetch(USER_AGENT, url):
                raise Forbidden(f"robots.txt disallows {url}")
            delay = host["delay"] - (time.monotonic() - host["last"])
            if delay > 0:
                time.sleep(min(delay, max(0, self.stop_at - time.time())))
            self.check()
            try:
                existing = destination.stat().st_size if destination and destination.exists() else 0
                headers = {"Range": f"bytes={existing}-"} if existing else {}
                try:
                    response = self.open(url, headers)
                except urllib.error.HTTPError as exc:
                    if exc.code == 416 and existing:
                        destination.unlink()
                    raise
                with response:
                    # Redirect targets must also satisfy their own robots policy.
                    # Official selected sources serve directly; reject unexpected redirects.
                    final = urllib.parse.urlsplit(response.url)
                    original = urllib.parse.urlsplit(url)
                    if final.netloc != original.netloc:
                        raise Forbidden(f"Unexpected cross-host redirect: {url} -> {response.url}")
                    append = existing and response.status == 206
                    if append:
                        content_range = response.headers.get("Content-Range", "")
                        if not content_range.startswith(f"bytes {existing}-"):
                            destination.unlink()
                            raise OSError("Server returned wrong resume offset; will retry full file")
                    expected = response.headers.get("Content-Length")
                    received, parts = 0, []
                    file = destination.open("ab" if append else "wb") if destination else None
                    try:
                        while True:
                            self.check()
                            # read1 returns after one buffered/socket read, allowing a
                            # slow stream to be interrupted at the fixed deadline.
                            raw_socket = getattr(getattr(getattr(response, "fp", None), "raw", None), "_sock", None)
                            if raw_socket is not None:
                                raw_socket.settimeout(max(0.1, min(5, self.stop_at - time.time())))
                            block = response.read1(256 * 1024)
                            if not block:
                                break
                            received += len(block)
                            if file:
                                file.write(block)
                            else:
                                parts.append(block)
                            with self.lock:
                                self.transferred += len(block)
                    finally:
                        if file:
                            file.close()
                    if expected and received != int(expected):
                        raise OSError(f"Incomplete HTTP body: {received}/{expected}")
                    return destination if destination else b"".join(parts)
            finally:
                host["last"] = time.monotonic()


class Collector:
    def __init__(self, root, hours):
        self.root = root.resolve()
        for name in ("midi", "archives", "evidence", "metadata"):
            (self.root / name).mkdir(parents=True, exist_ok=True)
        self.config_path = self.root / "config.json"
        if self.config_path.exists():
            self.config = json.loads(self.config_path.read_text(encoding="utf-8"))
        else:
            now = time.time()
            self.config = {"started_epoch": now, "stop_epoch": now + hours * 3600,
                           "started_at": timestamp(now), "stop_at": timestamp(now + hours * 3600),
                           "hours": hours, "file_limit": None, "timezone": "Asia/Shanghai"}
            atomic_json(self.config_path, self.config)
        self.file_limit = self.config.get("file_limit")
        if self.file_limit is not None and (type(self.file_limit) is not int or self.file_limit <= 0):
            raise ValueError("file_limit must be a positive integer or null")
        self.network = Network(self.root, self.config["stop_epoch"])
        self.db = sqlite3.connect(self.root / "state.sqlite", check_same_thread=False)
        self.db.execute("PRAGMA journal_mode=WAL")
        self.db.executescript("""
          CREATE TABLE IF NOT EXISTS jobs(url TEXT PRIMARY KEY, source TEXT, kind TEXT,
            metadata TEXT, state TEXT DEFAULT 'queued', attempts INTEGER DEFAULT 0,
            next_at REAL DEFAULT 0, error TEXT);
          CREATE TABLE IF NOT EXISTS files(sha256 TEXT PRIMARY KEY, path TEXT, bytes INTEGER, source TEXT, metadata TEXT);
          CREATE TABLE IF NOT EXISTS aliases(identity TEXT PRIMARY KEY, sha256 TEXT, metadata TEXT);
        """)
        self.db.execute("UPDATE jobs SET state='queued' WHERE state='running'")
        self.db.commit()
        self.db_lock = threading.RLock()
        self.unique_count = self.db.execute("SELECT COUNT(*) FROM files").fetchone()[0]
        if self.file_limit is not None and self.unique_count >= self.file_limit:
            self.network.stop_event.set()
        self.last_error = None
        self.active = {}
        self.archive_progress = {}
        self.register_seeds()

    def add(self, job):
        with self.db_lock:
            self.db.execute("INSERT OR IGNORE INTO jobs(url,source,kind,metadata) VALUES(?,?,?,?)",
                            (job["url"], job["source"], job["kind"], json.dumps(job.get("metadata", {}), ensure_ascii=False)))
            self.db.commit()

    def register_seeds(self):
        for job in sources.seed_jobs():
            self.add(job)
        self.add({"url": "https://storage.googleapis.com/magentadata/datasets/maestro/v3.0.0/maestro-v3.0.0-midi.zip",
                  "source": "maestro", "kind": "midi-zip", "metadata": {
                      "license": "CC BY-NC-SA 4.0", "license_url": "https://creativecommons.org/licenses/by-nc-sa/4.0/",
                      "source_page": "https://magenta.tensorflow.org/datasets/maestro", "attribution": "Google LLC; Curtis Hawthorne et al., ICLR 2019; MAESTRO v3.0.0",
                      "sha256": "70470ee253295c8d2c71e6d9d4a815189e35c89624b76d22fce5a019d5dde12c"}})
        self.add({"url": "https://codeload.github.com/fosfrancesco/asap-dataset/zip/afc815c75c42e83a79c03feb6da8a35e77d4c6b8",
                  "source": "asap", "kind": "midi-zip", "metadata": {
                      "license": "CC BY-NC-SA 4.0", "license_url": "https://creativecommons.org/licenses/by-nc-sa/4.0/",
                      "source_page": "https://github.com/fosfrancesco/asap-dataset",
                      "attribution": "Francesco Foscarin, Andrew McLeod, Philippe Rigaux, Florent Jacquemard, Masahiko Sakai; ASAP, ISMIR 2020"}})

    def save_midi(self, raw, job, member="", extra=None):
        info = validate_midi(raw)
        digest = hashlib.sha256(raw).hexdigest()
        metadata = {**job.get("metadata", {}), **(extra or {}), **info,
                    "download_url": job["url"], "source": job["source"], "downloaded_at": timestamp(),
                    "sha256": digest, "bytes": len(raw)}
        if member:
            metadata["archive_member"] = member
        identity = job["url"] + ("#" + member if member else "")
        with self.db_lock:
            # Check and commit under the same lock: concurrent downloads and
            # archive members cannot consume more than the remaining slots.
            self.network.check()
            if self.file_limit is not None and self.unique_count >= self.file_limit:
                self.network.stop_event.set()
                raise Stopped()
            previous = self.db.execute("SELECT path FROM files WHERE sha256=?", (digest,)).fetchone()
            destination, temporary, manifest_offset = None, None, None
            manifest = self.root / "manifest.jsonl"
            try:
                if previous:
                    relative = previous[0]
                    metadata["duplicate"] = True
                else:
                    composer = metadata.get("composer", "")
                    title = metadata.get("title") or Path(member or urllib.parse.unquote(urllib.parse.urlsplit(job["url"]).path)).stem
                    filename = safe_name(f"{composer} - {title}".strip(" -"), 100) + "__" + digest[:12] + ".mid"
                    relative = str(Path("midi") / safe_name(job["source"], 20) / filename)
                    destination = self.root / relative
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    temporary = destination.with_suffix(".part")
                    temporary.write_bytes(raw)
                    os.replace(temporary, destination)
                    self.db.execute("INSERT INTO files VALUES(?,?,?,?,?)", (digest, relative, len(raw), job["source"], json.dumps(metadata, ensure_ascii=False)))
                metadata["path"] = relative
                if not self.db.execute("SELECT 1 FROM aliases WHERE identity=?", (identity,)).fetchone():
                    self.db.execute("INSERT INTO aliases VALUES(?,?,?)", (identity, digest, json.dumps(metadata, ensure_ascii=False)))
                    with manifest.open("ab") as file:
                        manifest_offset = file.tell()
                        file.write((json.dumps(metadata, ensure_ascii=False) + "\n").encode("utf-8"))
                self.db.commit()
            except BaseException:
                # Never let finish_job commit a failed save without accounting
                # for it in the quota. Remove uncommitted files and manifest tail.
                self.db.rollback()
                for path in (temporary, destination):
                    if path is not None:
                        try:
                            path.unlink(missing_ok=True)
                        except OSError:
                            logging.exception("Could not remove incomplete MIDI %s", path)
                if manifest_offset is not None:
                    try:
                        with manifest.open("r+b") as file:
                            file.truncate(manifest_offset)
                    except OSError:
                        logging.exception("Could not roll back manifest tail")
                raise
            if not previous:
                self.unique_count += 1
                if self.file_limit is not None and self.unique_count >= self.file_limit:
                    self.network.stop_event.set()

    def archive(self, job):
        name = hashlib.sha256(job["url"].encode()).hexdigest()[:16]
        path = self.root / "archives" / (job["source"] + "-" + name + ".zip")
        if not path.exists():
            partial = path.with_suffix(".partial")
            self.network.fetch(job["url"], partial)
            wanted = job.get("metadata", {}).get("sha256")
            if wanted and hashlib.sha256(partial.read_bytes()).hexdigest() != wanted:
                partial.unlink()
                raise ValueError("Archive SHA256 does not match official checksum")
            if not zipfile.is_zipfile(partial):
                partial.unlink()
                raise ValueError("Response is not a ZIP archive")
            os.replace(partial, path)
        with zipfile.ZipFile(path) as archive:
            metadata_by_member = {}
            members = archive.namelist()
            for member in members:
                if member.lower().endswith("maestro-v3.0.0.csv"):
                    for row in csv.DictReader(io.StringIO(archive.read(member).decode("utf-8-sig"))):
                        key = "maestro-v3.0.0/" + row["midi_filename"]
                        metadata_by_member[key] = {"title": row["canonical_title"], "composer": row["canonical_composer"],
                                                   "year": row["year"], "duration_seconds": float(row["duration"])}
                elif job["source"] == "asap" and member.endswith("/metadata.csv"):
                    prefix = member.rsplit("/", 1)[0] + "/"
                    for row in csv.DictReader(io.StringIO(archive.read(member).decode("utf-8-sig"))):
                        for field in ("midi_score", "midi_performance"):
                            if row.get(field):
                                metadata_by_member[prefix + row[field]] = {"composer": row.get("composer", ""), "title": row.get("title", ""), "asap_role": field}
                basename = Path(member).name
                if basename.lower().endswith((".csv", ".json", ".md", ".txt")) and ("license" in basename.lower() or "readme" in basename.lower() or basename in ("metadata.csv", "maestro-v3.0.0.csv", "maestro-v3.0.0.json")):
                    (self.root / "metadata" / (job["source"] + "-" + safe_name(basename))).write_bytes(archive.read(member))
            midis = [member for member in members if member.lower().endswith((".mid", ".midi"))]
            rejected = 0
            for index, member in enumerate(midis):
                self.network.check()
                self.archive_progress[job["source"]] = {"processed": index, "members": len(midis)}
                fallback = {}
                if job["source"] == "asap" and member not in metadata_by_member:
                    parts = member.split("/")
                    fallback = {"composer": parts[1] if len(parts) > 2 else "", "title": " / ".join(parts[2:])}
                try:
                    self.save_midi(archive.read(member), job, member, metadata_by_member.get(member, fallback))
                except (ValueError, struct.error) as exc:
                    rejected += 1
                    logging.warning("Rejected MIDI %s#%s: %s", job["url"], member, exc)
                    with self.db_lock, (self.root / "rejected.jsonl").open("a", encoding="utf-8") as file:
                        file.write(json.dumps({"url": job["url"], "member": member, "error": str(exc)}, ensure_ascii=False) + "\n")
            self.archive_progress[job["source"]] = {"processed": len(midis), "members": len(midis), "rejected": rejected}
            logging.info("Archive complete: %s; MIDI=%s, rejected=%s", job["source"], len(midis), rejected)

    def process(self, job):
        if job["kind"] == "midi-zip":
            self.archive(job)
        elif job["kind"] == "midi":
            self.save_midi(self.network.fetch(job["url"]), job)
        else:
            raw = self.network.fetch(job["url"])
            key = hashlib.sha256(job["url"].encode()).hexdigest()[:20]
            (self.root / "evidence" / (key + ".html")).write_bytes(raw)
            for discovered in sources.process_page(job, raw):
                self.add(discovered)

    def finish_job(self, job, future):
        try:
            future.result()
            state, error, delay = "done", None, 0
        except Stopped:
            state, error, delay = "queued", "File limit, deadline or STOP requested", 0
        except Forbidden as exc:
            state, error, delay = "excluded", str(exc), 0
        except urllib.error.HTTPError as exc:
            error = str(exc)
            if exc.code in (404, 410):
                state, delay = "unavailable", 0
            else:
                state, delay = "retry", min(3600, 60 * 2 ** min(job["attempts"], 6))
                retry_after = exc.headers.get("Retry-After")
                if retry_after:
                    try:
                        delay = max(delay, int(retry_after))
                    except ValueError:
                        try:
                            delay = max(delay, email.utils.parsedate_to_datetime(retry_after).timestamp() - time.time())
                        except (TypeError, ValueError):
                            pass
        except (ValueError, struct.error, zipfile.BadZipFile) as exc:
            state, error, delay = "invalid", str(exc), 0
        except Exception as exc:
            state, error = "retry", f"{type(exc).__name__}: {exc}"
            delay = min(3600, 60 * 2 ** min(job["attempts"], 6))
        if error:
            self.last_error = {"at": timestamp(), "url": job["url"], "state": state, "error": error}
            logging.warning("%s %s: %s", state, job["url"], error)
        with self.db_lock:
            self.db.execute("UPDATE jobs SET state=?, error=?, next_at=? WHERE url=?", (state, error, time.time() + delay, job["url"]))
            self.db.commit()

    def status(self, state="running"):
        with self.db_lock:
            counts = dict(self.db.execute("SELECT state,COUNT(*) FROM jobs GROUP BY state"))
            files, size = self.db.execute("SELECT COUNT(*),COALESCE(SUM(bytes),0) FROM files").fetchone()
            aliases = self.db.execute("SELECT COUNT(*) FROM aliases").fetchone()[0]
            by_source = {source: {"files": count, "bytes": size} for source, count, size in self.db.execute("SELECT source,COUNT(*),SUM(bytes) FROM files GROUP BY source")}
        stop_reason = None
        if self.file_limit is not None and files >= self.file_limit:
            stop_reason = "file_limit"
        elif (self.root / "STOP").exists():
            stop_reason = "stop_requested"
        elif time.time() >= self.config["stop_epoch"]:
            stop_reason = "deadline"
        data = {**self.config, "state": state, "stop_reason": stop_reason, "updated_at": timestamp(), "pid": os.getpid(),
                "unique_midi": files, "midi_bytes": size, "source_records": aliases,
                "duplicates": aliases - files, "sources": by_source, "jobs": counts,
                "active": [{"source": job["source"], "kind": job["kind"], "url": job["url"]} for job in self.active.values()],
                "archive_progress": dict(self.archive_progress), "last_error": self.last_error,
                "remaining_seconds": max(0, round(self.config["stop_epoch"] - time.time())),
                "stop_requested": (self.root / "STOP").exists()}
        atomic_json(self.root / "status.json", data)
        return data

    def run(self):
        atomic_json(self.root / "process.json", {"pid": os.getpid(), "started_at": timestamp()})
        self.status()
        logging.info("Collection started; stop at %s; unique MIDI limit=%s", self.config["stop_at"], self.file_limit)
        last_status, last_rescan = 0, time.time()
        final_state = "completed"
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            try:
                while True:
                    self.network.check()
                    for future, job in list(self.active.items()):
                        if future.done():
                            self.finish_job(job, future)
                            del self.active[future]
                    while len(self.active) < 4:
                        with self.db_lock:
                            row = self.db.execute("SELECT url,source,kind,metadata,attempts FROM jobs WHERE state IN ('queued','retry') AND next_at<=? ORDER BY CASE WHEN kind IN ('midi','midi-zip') THEN 0 ELSE 1 END,rowid LIMIT 1", (time.time(),)).fetchone()
                            if not row:
                                break
                            url, source, kind, metadata, attempts = row
                            self.db.execute("UPDATE jobs SET state='running',attempts=attempts+1 WHERE url=?", (url,))
                            self.db.commit()
                        job = {"url": url, "source": source, "kind": kind, "metadata": json.loads(metadata), "attempts": attempts}
                        self.active[pool.submit(self.process, job)] = job
                    if time.time() - last_status >= 10:
                        self.status()
                        last_status = time.time()
                    # If finite catalogs are exhausted early, recheck their indexes hourly.
                    # Completed MIDI URLs and SHA256 hashes stay deduplicated.
                    if time.time() - last_rescan >= 3600:
                        with self.db_lock:
                            self.db.execute("UPDATE jobs SET state='queued', next_at=0 WHERE kind NOT IN ('midi','midi-zip') AND state='done'")
                            self.db.commit()
                        last_rescan = time.time()
                        logging.info("Hourly catalog recheck queued")
                    time.sleep(min(0.5, max(0, self.config["stop_epoch"] - time.time())))
            except Stopped:
                final_state = "stopped" if (self.root / "STOP").exists() else "completed"
            except BaseException:
                final_state = "failed"
                logging.exception("Collector interrupted unexpectedly")
                raise
            finally:
                # All in-flight reads have a bounded timeout and check the same deadline.
                for future, job in list(self.active.items()):
                    self.finish_job(job, future)
                    del self.active[future]
                data = self.status(final_state)
                atomic_json(self.root / "summary.json", data)
                logging.info("Collection %s; unique MIDI=%s", final_state, data["unique_midi"])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--hours", type=float, default=8)
    args = parser.parse_args()
    if args.hours <= 0:
        parser.error("--hours must be positive")
    args.output.mkdir(parents=True, exist_ok=True)
    logging.basicConfig(filename=args.output / "collector.log", level=logging.INFO,
                        format="%(asctime)s %(levelname)s %(message)s", encoding="utf-8")
    # A process-scoped OS file lock prevents a monitor from starting duplicates.
    lock = (args.output / "collector.lock").open("a+b")
    if lock.tell() == 0:
        lock.write(b"0")
        lock.flush()
    lock.seek(0)
    try:
        import msvcrt
        msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
    except OSError:
        return
    try:
        Collector(args.output, args.hours).run()
    finally:
        lock.close()


if __name__ == "__main__":
    main()
