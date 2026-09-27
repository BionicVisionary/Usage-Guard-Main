import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { assess, deliver, readObservation } from '../scripts/agent-alerts/usage-guard-alert.mjs';

const now = Date.parse('2026-09-11T01:00:00Z');
const root = fs.mkdtempSync('D:/Codex/Artifacts/UsageGuard/agent-alert-tests-');
test('hook installer preserves unrelated hooks, is idempotent and refuses conflicts', () => {
  const config = path.join(root, 'install-config');
  fs.mkdirSync(config);
  const hooksFile = path.join(config, 'hooks.json');
  const unrelated = { hooks: [{ type: 'command', command: 'echo unrelated' }] };
  fs.writeFileSync(hooksFile, JSON.stringify({ description: 'keep me', hooks: { Stop: [unrelated] } }));
  const installer = fileURLToPath(new URL('../scripts/agent-alerts/Install-CodexAlerts.ps1', import.meta.url));
  const run = () => spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
    '-File', installer, '-NodePath', process.execPath, '-CodexConfigDirectory', config], { encoding: 'utf8', timeout: 30000 });
  let result = run();
  assert.equal(result.status, 0, result.stderr);
  let installed = JSON.parse(fs.readFileSync(hooksFile, 'utf8').replace(/^\uFEFF/, ''));
  assert.equal(installed.description, 'keep me');
  assert.deepEqual(installed.hooks.Stop, [unrelated]);
  assert.equal(installed.hooks.PreToolUse.length, 1);
  assert.equal(installed.hooks.PostToolUse.length, 1);
  assert.equal(installed.hooks.UserPromptSubmit.length, 1);
  assert.equal(installed.hooks.PreToolUse[0].hooks[0].timeout, 3);
  // Test the generated Windows command, not just its JSON definition.
  const local = path.join(root, 'launch-settings');
  const guard = path.join(local, 'OpenAI', 'CodexUsageGuard'); fs.mkdirSync(guard, { recursive: true });
  fs.writeFileSync(path.join(guard, 'settings.json'), JSON.stringify({ ...settings, unrestrictedDevelopmentOverride: true }));
  const launch = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', installed.hooks.UserPromptSubmit[0].hooks[0].command], {
    input: JSON.stringify({ hook_event_name: 'UserPromptSubmit', session_id: 'isolated-launch', turn_id: 'isolated-launch' }),
    env: { ...process.env, LOCALAPPDATA: local }, encoding: 'utf8', timeout: 10000
  });
  assert.equal(launch.status, 0, launch.stderr);
  assert.match(JSON.parse(launch.stdout).hookSpecificOutput.additionalContext, /restrictions are lifted/);
  const before = fs.readFileSync(hooksFile);
  result = run(); assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(fs.readFileSync(hooksFile), before);
  const correctCommand = installed.hooks.UserPromptSubmit[0].hooks[0].command;
  // Regression for the 2026-09-21 installer: bare quoted executable in PS.
  const receiverPath = fileURLToPath(new URL('../scripts/agent-alerts/usage-guard-alert.mjs', import.meta.url));
  installed.hooks.UserPromptSubmit[0].hooks[0].command = `"${process.execPath}" "${receiverPath}"`;
  const originalPre = JSON.stringify(installed.hooks.PreToolUse);
  fs.writeFileSync(hooksFile, JSON.stringify(installed));
  result = run(); assert.equal(result.status, 0, result.stderr);
  installed = JSON.parse(fs.readFileSync(hooksFile, 'utf8'));
  assert.equal(installed.hooks.UserPromptSubmit[0].hooks[0].command, correctCommand);
  assert.equal(JSON.stringify(installed.hooks.PreToolUse), originalPre);
  // An older reviewed definition used the same Node path without quotes.
  // Preserve it exactly, while adding a missing prompt hook.
  if (!/[\s&|<>^();`$]/.test(process.execPath)) {
    const legacy = installed.hooks.PreToolUse[0].hooks[0].command.replace(/^"([^"]+)" /, '$1 ');
    installed.hooks.PreToolUse[0].hooks[0].command = legacy;
    delete installed.hooks.UserPromptSubmit;
    fs.writeFileSync(hooksFile, JSON.stringify(installed));
    result = run(); assert.equal(result.status, 0, result.stderr);
    installed = JSON.parse(fs.readFileSync(hooksFile, 'utf8'));
    assert.equal(installed.hooks.PreToolUse[0].hooks[0].command, legacy);
    assert.equal(installed.hooks.UserPromptSubmit.length, 1);
  }
  installed.hooks.PreToolUse[0].hooks[0].command = 'different usage-guard-alert.mjs';
  fs.writeFileSync(hooksFile, JSON.stringify(installed));
  const conflicting = fs.readFileSync(hooksFile);
  result = run(); assert.notEqual(result.status, 0);
  assert.deepEqual(fs.readFileSync(hooksFile), conflicting);
});
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
test('normal and explicit override provide scoped recovery notices', () => {
  assert.match(assess(settings, state(), now).message, /restrictions are lifted/);
  assert.match(assess({ ...settings, unrestrictedDevelopmentOverride: true }, null, now).message, /not evidence of a quota reset/);
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
  assert.match(deliver(event, assess(settings, state(), now), dir, now + 1).hookSpecificOutput.additionalContext, /restrictions are lifted/);
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

test('installer invokes an executable path containing spaces with PowerShell call operator', () => {
  const config = path.join(root, 'spaced-config'); fs.mkdirSync(config);
  // A tiny forwarding shim avoids copying the Node binary or requiring symlinks.
  const shim = path.join(root, 'node test shim.cmd');
  fs.writeFileSync(shim, `@"${process.execPath}" %*\r\n`);
  const installer = fileURLToPath(new URL('../scripts/agent-alerts/Install-CodexAlerts.ps1', import.meta.url));
  const setup = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-File', installer,
    '-NodePath', shim, '-CodexConfigDirectory', config], { encoding: 'utf8', timeout: 10000 });
  assert.equal(setup.status, 0, setup.stderr);
  const command = JSON.parse(fs.readFileSync(path.join(config, 'hooks.json'), 'utf8')).hooks.UserPromptSubmit[0].hooks[0].command;
  assert.ok(command.startsWith('& "'));
  const local = path.join(root, 'space-launch-state');
  const guard = path.join(local, 'OpenAI', 'CodexUsageGuard'); fs.mkdirSync(guard, { recursive: true });
  fs.writeFileSync(path.join(guard, 'settings.json'), JSON.stringify({ ...settings, unrestrictedDevelopmentOverride: true }));
  const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', command], {
    input: JSON.stringify({ ...event, hook_event_name: 'UserPromptSubmit' }),
    env: { ...process.env, LOCALAPPDATA: local }, encoding: 'utf8', timeout: 10000
  });
  assert.equal(result.status, 0, result.stderr);
  assert.match(JSON.parse(result.stdout).hookSpecificOutput.additionalContext, /restrictions are lifted/);
});

test('idle resume delivers current recovery without a tool or any ledger access', () => {
  const dir = path.join(root, 'not-a-ledger-directory'); fs.writeFileSync(dir, 'untouched');
  const prompt = { ...event, hook_event_name: 'UserPromptSubmit', prompt: 'PRIVATE_PROMPT' };
  for (const observation of [assess(settings, state(), now),
    assess(settings, state('warning', 30), now),
    assess({ ...settings, unrestrictedDevelopmentOverride: true }, null, now)]) {
    for (let i = 0; i < 2; i++) {
      const out = deliver(prompt, observation, dir, now + i);
      assert.equal(out.hookSpecificOutput.hookEventName, 'UserPromptSubmit');
      assert.match(out.hookSpecificOutput.additionalContext, /restrictions are lifted/);
      assert.match(out.hookSpecificOutput.additionalContext, /supersedes only earlier Usage Guard/);
      assert.equal(out.decision, undefined); assert.equal(out.continue, undefined);
      assert.doesNotMatch(JSON.stringify(out), /PRIVATE_PROMPT/);
    }
  }
  assert.equal(fs.readFileSync(dir, 'utf8'), 'untouched');
});

test('tool recovery follows every restrictive state; steady allowing states are quiet', () => {
  for (const [i, restriction] of [assess(settings, state('safe_wrap', 25), now),
    assess(settings, state('safe_wrap', 20), now), assess(settings, null, now)].entries()) {
    for (const recovery of [assess(settings, state(), now),
      assess({ ...settings, unrestrictedDevelopmentOverride: true }, null, now),
      assess(settings, state('warning', 30), now)]) {
      const dir = path.join(root, `transition-${i}-${recovery.level}`);
      assert.ok(deliver(event, restriction, dir, now).hookSpecificOutput);
      assert.match(deliver(event, recovery, dir, now + 1).hookSpecificOutput.additionalContext, /restrictions are lifted/);
      assert.deepEqual(deliver({ ...event, hook_event_name: 'PostToolUse' }, recovery, dir, now + 2), {});
      if (recovery.level !== 'warning') assert.deepEqual(deliver({ ...event, turn_id: 'next' }, recovery, dir, now + 3), {});
    }
  }
});

test('prompt recovery is not hidden by old receipts, active locks, or earlier delivery', () => {
  const dir = path.join(root, 'old-ledger'); fs.mkdirSync(dir);
  const ledger = path.join(dir, crypto.createHash('sha256').update(event.session_id).digest('hex') + '.json');
  fs.writeFileSync(ledger, JSON.stringify({ key: `${event.turn_id}:normal:`, level: 'normal', emittedAt: now }));
  const normal = assess(settings, state(), now);
  assert.ok(deliver(event, normal, dir, now).hookSpecificOutput);
  fs.writeFileSync(ledger + '.lock', '');
  const prompt = { ...event, hook_event_name: 'UserPromptSubmit' };
  assert.match(deliver(prompt, normal, dir, now).hookSpecificOutput.additionalContext, /restrictions are lifted/);
  assert.match(deliver(prompt, normal, dir, now + 1).hookSpecificOutput.additionalContext, /restrictions are lifted/);
  fs.unlinkSync(ledger + '.lock');
  fs.writeFileSync(ledger, 'broken');
  assert.ok(deliver(event, normal, dir, now).hookSpecificOutput);
});

test('override off reapplies stop and stale data never claims recovery', () => {
  const dir = path.join(root, 'override-off');
  deliver(event, assess({ ...settings, unrestrictedDevelopmentOverride: true }, null, now), dir, now);
  const stopped = deliver(event, assess(settings, state('safe_wrap', 80, 5), now), dir, now + 1);
  assert.match(stopped.hookSpecificOutput.additionalContext, /Critical SafeWrap: urgently/);
  for (const bad of [assess(settings, state(), now + 120001), assess(settings, null, now),
    assess({ ...settings, unrestrictedDevelopmentOverride: 'yes' }, null, now)]) {
    const msg = deliver({ ...event, hook_event_name: 'UserPromptSubmit' }, bad, dir, now).hookSpecificOutput.additionalContext;
    assert.match(msg, /No recovery is established/);
    assert.doesNotMatch(msg, /restrictions are lifted/);
  }
});

test('real stdin prompt protocol returns recovery without persisting a prompt or receipt', () => {
  const local = path.join(root, 'prompt-process');
  const dir = path.join(local, 'OpenAI', 'CodexUsageGuard'); fs.mkdirSync(dir, { recursive: true });
  const configured = { ...settings, unrestrictedDevelopmentOverride: true };
  fs.writeFileSync(path.join(dir, 'settings.json'), JSON.stringify(configured));
  const proc = spawnSync(process.execPath, [fileURLToPath(new URL('../scripts/agent-alerts/usage-guard-alert.mjs', import.meta.url))], {
    input: JSON.stringify({ ...event, hook_event_name: 'UserPromptSubmit', prompt: 'PRIVATE_PROMPT' }),
    encoding: 'utf8', timeout: 3000, env: { ...process.env, LOCALAPPDATA: local }
  });
  assert.equal(proc.status, 0, proc.stderr);
  const output = JSON.parse(proc.stdout);
  assert.equal(output.hookSpecificOutput.hookEventName, 'UserPromptSubmit');
  assert.match(output.hookSpecificOutput.additionalContext, /restrictions are lifted/);
  assert.doesNotMatch(proc.stdout, /PRIVATE_PROMPT/);
  assert.deepEqual(fs.readdirSync(dir), ['settings.json']);
});
