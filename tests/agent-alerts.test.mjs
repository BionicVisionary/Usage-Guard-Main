import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { assess, deliver, readObservation } from '../scripts/agent-alerts/usage-guard-alert.mjs';

const now = Date.parse('2026-09-11T01:00:00Z');
const root = fs.mkdtempSync('D:/Codex/Artifacts/UsageGuard/agent-alert-tests-');
const settings = { schemaVersion: 1, warningThresholdPercent: 15, safeWrapThresholdPercent: 10,
  criticalBufferPercent: 5, fiveHourWarningThresholdPercent: 30, fiveHourSafeWrapThresholdPercent: 25,
  fiveHourCriticalBufferPercent: 20, pollingIntervalSeconds: 30, unrestrictedDevelopmentOverride: false };
function state(decision = 'normal', five = 80, weekly = 40) {
  return { schemaVersion: 1, current: { decision, reason: decision === 'safe_wrap' ? 'safe_wrap_threshold_reached' : 'warning_threshold_reached',
    observedAtUtc: new Date(now).toISOString(), confidence: 'high', freshness: 'observed_now',
    source: 'live_app_server', isSuccessfulLiveObservation: true, startNewPhaseAllowed: decision !== 'safe_wrap',
    finishCurrentCheckpointOnly: decision === 'safe_wrap', windows: [
      { kind: 'five_hour', remainingPercent: five, resetsAtUtc: '2026-09-11T04:00:00Z' },
      { kind: 'weekly', remainingPercent: weekly, resetsAtUtc: '2026-09-15T04:00:00Z' }
    ] } };
}
const event = { hook_event_name: 'PreToolUse', session_id: 'test-session', turn_id: 'test-turn',
  tool_input: { command: 'SECRET_DO_NOT_PERSIST' } };
test('normal is silent and explicit user override is respected', () => {
  assert.equal(assess(settings, state(), now).message, '');
  assert.equal(assess({ ...settings, unrestrictedDevelopmentOverride: true }, state(), now).level, 'override');
  assert.equal(assess({ ...settings, unrestrictedDevelopmentOverride: true }, null, now).level, 'override');
});
test('equal thresholds accepted by the helper stay accepted by the receiver', () => {
  const equal = { ...settings, warningThresholdPercent: 10, safeWrapThresholdPercent: 10, criticalBufferPercent: 10 };
  assert.equal(assess(equal, state('safe_wrap', 80, 10), now).level, 'critical_safe_wrap');
});
test('threshold boundaries use the user values for both windows', () => {
  assert.equal(assess(settings, state('warning', 30), now).level, 'warning');
  assert.equal(assess(settings, state('safe_wrap', 25), now).level, 'safe_wrap');
  assert.equal(assess(settings, state('safe_wrap', 20), now).level, 'critical_safe_wrap');
  assert.equal(assess(settings, state('warning', 80, 15), now).level, 'warning');
  assert.equal(assess(settings, state('safe_wrap', 80, 10), now).level, 'safe_wrap');
  assert.equal(assess(settings, state('safe_wrap', 80, 5), now).level, 'critical_safe_wrap');
  assert.match(assess(settings, state('warning', 80, 15), now).message, /15\/10\/5%/);
});
test('durable latch still alerts and critical escalation survives latch reason', () => {
  const s = state('safe_wrap', 80, 40);
  s.current.source = 'genuine_live_latch'; s.current.reason = 'genuine_latch_active';
  assert.equal(assess(settings, s, now).level, 'safe_wrap');
  s.current.windows[0].remainingPercent = 20;
  assert.equal(assess(settings, s, now).level, 'critical_safe_wrap');
});
test('persisted observed_now cannot disguise stale, future or expired readings', () => {
  assert.equal(assess(settings, state(), now + 120001).level, 'unknown');
  assert.equal(assess(settings, state(), now - 5001).level, 'unknown');
  const s = state(); s.current.windows[0].resetsAtUtc = new Date(now).toISOString();
  assert.equal(assess(settings, s, now).level, 'unknown');
});
test('duplicate, missing and invalid windows fail visibly', () => {
  for (const mutate of [s => s.current.windows.pop(), s => s.current.windows[1] = s.current.windows[0],
    s => s.current.windows[0].remainingPercent = -1, s => s.current.windows[1].kind = 'other',
    s => s.current.windows[0].resetsAtUtc = 'bad']) {
    const s = state(); mutate(s); assert.equal(assess(settings, s, now).level, 'unknown');
  }
});
test('contradictory admission, provenance and changed settings cannot silently allow work', () => {
  const s = state('safe_wrap', 20); s.current.startNewPhaseAllowed = true;
  assert.equal(assess(settings, s, now).level, 'unknown');
  s.current.source = 'unavailable'; assert.equal(assess(settings, s, now).level, 'unknown');
  assert.equal(assess(settings, state('normal', 25), now).level, 'unknown');
  assert.equal(assess({ ...settings, safeWrapThresholdPercent: 90 }, state(), now).level, 'unknown');
});
test('alerts are deduplicated across pre/post, escalate and remain task-specific', () => {
  const dir = path.join(root, 'delivery');
  const warning = assess(settings, state('warning', 30), now);
  assert.match(deliver(event, warning, dir, now).hookSpecificOutput.additionalContext, /Warning/);
  assert.deepEqual(deliver({ ...event, hook_event_name: 'PostToolUse' }, warning, dir, now + 10), {});
  const safe = assess(settings, state('safe_wrap', 25), now);
  assert.match(deliver(event, safe, dir, now + 20).hookSpecificOutput.additionalContext, /SafeWrap/);
  assert.deepEqual(deliver(event, safe, dir, now + 40), {});
  assert.ok(deliver(event, safe, dir, now + 60020).hookSpecificOutput);
  assert.ok(deliver(event, assess(settings, state('safe_wrap', 20), now), dir, now + 60030).hookSpecificOutput);
  assert.ok(deliver({ ...event, session_id: 'other-session' }, warning, dir, now).hookSpecificOutput);
  assert.ok(deliver({ ...event, turn_id: 'next-turn' }, warning, dir, now).hookSpecificOutput);
  for (const name of fs.readdirSync(dir)) assert.doesNotMatch(fs.readFileSync(path.join(dir, name), 'utf8'), /SECRET_DO_NOT_PERSIST/);
});
test('normal recovery rearms an alert and corrupt receipt does not hide warning', () => {
  const dir = path.join(root, 'recovery'); const warning = assess(settings, state('warning', 30), now);
  deliver(event, warning, dir, now);
  assert.deepEqual(deliver(event, assess(settings, state(), now), dir, now + 1), {});
  assert.ok(deliver(event, warning, dir, now + 2).hookSpecificOutput);
  fs.writeFileSync(path.join(dir, fs.readdirSync(dir)[0]), 'broken');
  assert.ok(deliver(event, warning, dir, now + 3).hookSpecificOutput);
});
test('unsupported hook events never request continuation or write state', () => {
  const dir = path.join(root, 'no-stop-hook');
  assert.deepEqual(deliver({ ...event, hook_event_name: 'Stop' }, unknownForTest(), dir, now), {});
  assert.equal(fs.existsSync(dir), false);
  assert.deepEqual(deliver({ ...event, session_id: '' }, unknownForTest(), dir, now), {});
});
function unknownForTest() { return { level: 'unknown', message: 'test' }; }
test('missing, malformed and newly changed local settings are visible', () => {
  const dir = path.join(root, 'reader'); fs.mkdirSync(dir);
  assert.equal(readObservation(dir, now).level, 'unknown');
  fs.writeFileSync(path.join(dir, 'settings.json'), JSON.stringify(settings));
  fs.writeFileSync(path.join(dir, 'state.json'), JSON.stringify(state('warning', 30)));
  assert.equal(readObservation(dir, now).level, 'warning');
  const later = new Date(Date.now() + 5000); fs.utimesSync(path.join(dir, 'settings.json'), later, later);
  assert.equal(readObservation(dir, now).level, 'unknown');
});
test('read-only state files stay byte identical', () => {
  const dir = path.join(root, 'readonly'); fs.mkdirSync(dir);
  const a = JSON.stringify(settings), b = JSON.stringify(state());
  fs.writeFileSync(path.join(dir, 'settings.json'), a); fs.writeFileSync(path.join(dir, 'state.json'), b);
  readObservation(dir, now);
  assert.equal(fs.readFileSync(path.join(dir, 'settings.json'), 'utf8'), a);
  assert.equal(fs.readFileSync(path.join(dir, 'state.json'), 'utf8'), b);
});
test('cached fallback executable returns JSON without network or writes to input files', () => {
  const local = path.join(root, 'subprocess');
  const dir = path.join(local, 'OpenAI', 'CodexUsageGuard'); fs.mkdirSync(dir, { recursive: true });
  const s = state('warning', 30); s.current.observedAtUtc = new Date().toISOString();
  for (const w of s.current.windows) w.resetsAtUtc = new Date(Date.now() + 3600000).toISOString();
  fs.writeFileSync(path.join(dir, 'settings.json'), JSON.stringify(settings));
  fs.writeFileSync(path.join(dir, 'state.json'), JSON.stringify(s));
  const proc = spawnSync(process.execPath, [fileURLToPath(new URL('../scripts/agent-alerts/usage-guard-alert.mjs', import.meta.url)), '--check'],
    { encoding: 'utf8', timeout: 3000, env: { ...process.env, LOCALAPPDATA: local, CODEX_THREAD_ID: '' } });
  assert.equal(proc.status, 0, proc.stderr);
  const result = JSON.parse(proc.stdout);
  assert.equal(result.level, 'warning'); assert.equal(result.liveQuotaRequest, false);
  assert.equal(result.delivery, 'unverified_use_checkpoint_fallback');
  assert.deepEqual(fs.readdirSync(dir).sort(), ['settings.json', 'state.json']);
});
test('a new settings override is honored even without a state file', () => {
  const dir = path.join(root, 'override'); fs.mkdirSync(dir);
  fs.writeFileSync(path.join(dir, 'settings.json'), JSON.stringify({ ...settings, unrestrictedDevelopmentOverride: true }));
  assert.equal(readObservation(dir, now).level, 'override');
});
