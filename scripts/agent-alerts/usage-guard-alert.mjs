// Local, dependency-free Codex hook. Never launches a model, provider, or task.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const EVENTS = new Set(['PreToolUse', 'PostToolUse']);
const MAX_FILE = 256 * 1024;
const percent = n => typeof n === 'number' && Number.isFinite(n) && n >= 0 && n <= 100;
const unknown = why => ({ level: 'unknown', message: `Usage Guard: monitoring unavailable (${why}). Finish the current safe checkpoint; start no new phase. Use the installed guard once to diagnose/refresh (one 30-second retry only for startup timeout). Do not change thresholds or create a wake-up.` });

export function assess(settings, state, now = Date.now()) {
  if (settings?.schemaVersion !== 1) return unknown('invalid schema');
  const limits = {
    five_hour: [settings.fiveHourWarningThresholdPercent, settings.fiveHourSafeWrapThresholdPercent, settings.fiveHourCriticalBufferPercent],
    weekly: [settings.warningThresholdPercent, settings.safeWrapThresholdPercent, settings.criticalBufferPercent]
  };
  if (Object.values(limits).some(a => !a.every(percent) || !(a[0] >= a[1] && a[1] >= a[2])) ||
      !Number.isInteger(settings.pollingIntervalSeconds) || settings.pollingIntervalSeconds < 30 || settings.pollingIntervalSeconds > 300 ||
      typeof settings.unrestrictedDevelopmentOverride !== 'boolean') return unknown('invalid settings');
  if (settings.unrestrictedDevelopmentOverride) return { level: 'override', message: '' };
  if (state?.schemaVersion !== 1) return unknown('invalid schema');
  const c = state.current;
  if (!c || ['unknown', 'provenance_mismatch'].includes(c.decision)) return unknown('helper has no trusted decision');
  const age = now - Date.parse(c.observedAtUtc);
  // Respect the helper's two-minute freshness ceiling; never trust a persisted
  // "observed_now" label indefinitely after the monitoring process stops.
  if (!Number.isFinite(age) || age < -5000 || age > 120000) return unknown('observation is stale');
  const windows = c.windows;
  if (!Array.isArray(windows) || windows.length !== 2 ||
      new Set(windows.map(w => w.kind)).size !== 2 ||
      windows.some(w => !Object.hasOwn(limits, w.kind) || !percent(w.remainingPercent) ||
        !Number.isFinite(Date.parse(w.resetsAtUtc)) || Date.parse(w.resetsAtUtc) <= now)) return unknown('invalid quota windows');
  if (c.confidence !== 'high' || c.freshness !== 'observed_now' || c.isSuccessfulLiveObservation !== true ||
      !['live_app_server', 'genuine_live_latch'].includes(c.source)) return unknown('untrusted observation');
  if (!['normal', 'warning', 'safe_wrap'].includes(c.decision) ||
      c.startNewPhaseAllowed !== (c.decision !== 'safe_wrap') ||
      c.finishCurrentCheckpointOnly !== (c.decision === 'safe_wrap') ||
      (c.source === 'genuine_live_latch' && c.decision !== 'safe_wrap')) return unknown('contradictory decision');
  // The helper decides admission. Validate that a saved decision has not fallen
  // behind a more restrictive user setting; do not substitute agent defaults.
  const atSafe = windows.some(w => w.remainingPercent <= limits[w.kind][1]);
  const atWarning = windows.some(w => w.remainingPercent <= limits[w.kind][0]);
  if ((atSafe && c.decision !== 'safe_wrap') || (atWarning && c.decision === 'normal')) return unknown('decision awaits applied settings');
  if (c.decision === 'normal') return { level: 'normal', message: '' };
  const critical = c.decision === 'safe_wrap' && (c.reason === 'critical_buffer_reached' ||
    windows.some(w => w.remainingPercent <= limits[w.kind][2]));
  const level = critical ? 'critical_safe_wrap' : c.decision;
  const summary = ['five_hour', 'weekly'].map(kind => {
    const w = windows.find(w => w.kind === kind);
    return `${kind === 'five_hour' ? '5-hour' : 'weekly'} ${w.remainingPercent}% remaining; configured Warning/SafeWrap/Critical ${limits[kind].join('/')}%`;
  }).join('. ');
  const action = level === 'warning'
    ? 'Warning: use short recoverable checkpoints, avoid a large new phase, and prepare a concise handoff. Follow verified hook alerts; until delivery is verified, keep the cached checkpoint fallback. Do not poll live quotas.'
    : `${critical ? 'Critical SafeWrap: urgently' : 'SafeWrap:'} finish only the current coherent checkpoint, save the handoff, do necessary cleanup and become idle. Start no new phase or delegation. Do not interrupt commands, kill tasks, change settings, or schedule a wake-up.`;
  return { level, signature: JSON.stringify(limits), message: `Usage Guard — ${action} ${summary}. Observed ${new Date(c.observedAtUtc).toISOString()}.` };
}

function readJson(file) {
  const stat = fs.lstatSync(file);
  if (!stat.isFile() || stat.isSymbolicLink() || stat.size > MAX_FILE) throw new Error('invalid file');
  return JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
}

export function readObservation(guardDir, now = Date.now()) {
  try {
    const settingsFile = path.join(guardDir, 'settings.json');
    const stateFile = path.join(guardDir, 'state.json');
    const settings = readJson(settingsFile);
    if (settings.unrestrictedDevelopmentOverride === true) return assess(settings, null, now);
    const state = readJson(stateFile);
    // Do not pair a new settings write with a decision produced before it.
    if (!settings.unrestrictedDevelopmentOverride && fs.statSync(settingsFile).mtimeMs > fs.statSync(stateFile).mtimeMs + 1000)
      return unknown('settings changed after the last decision');
    return assess(settings, state, now);
  } catch { return unknown('local state missing or unreadable'); }
}

export function deliver(event, observation, ledgerDir, now = Date.now()) {
  if (!EVENTS.has(event?.hook_event_name) || typeof event.session_id !== 'string' ||
      !event.session_id || event.session_id.length > 200 || typeof event.turn_id !== 'string' ||
      !event.turn_id || event.turn_id.length > 200) return {};
  const id = crypto.createHash('sha256').update(event.session_id).digest('hex');
  fs.mkdirSync(ledgerDir, { recursive: true });
  if (fs.lstatSync(ledgerDir).isSymbolicLink()) throw new Error('invalid ledger');
  const ledgerFile = path.join(ledgerDir, `${id}.json`);
  // Exclusive lock serializes parallel pre/post hooks. Contended calls remain
  // silent; the next tool boundary retries. Never wait on or remove another lock.
  const lock = `${ledgerFile}.lock`;
  let fd;
  try { fd = fs.openSync(lock, 'wx'); } catch (e) {
    if (e.code === 'EEXIST' && now - fs.lstatSync(lock).mtimeMs < 10000) return {};
    if (e.code === 'EEXIST') return { hookSpecificOutput: { hookEventName: event.hook_event_name,
      additionalContext: 'Usage Guard alert receiver is busy or its lock is stale. At the next safe checkpoint use the cached fallback if no threshold alert arrives; do not assume protection is active.' } };
    throw e;
  }
  try {
    let previous = {};
    try { previous = readJson(ledgerFile); } catch { /* first event or corrupt receipt */ }
    const key = `${event.turn_id}:${observation.level}:${observation.signature ?? ''}`;
    const urgent = ['safe_wrap', 'critical_safe_wrap'].includes(observation.level);
    const repeat = urgent && now - (previous.emittedAt ?? 0) >= 60000;
    const changed = previous.key !== key;
    const emit = !!observation.message && (changed || repeat);
    // A minute-spaced health receipt lets a task distinguish working hooks from
    // merely installed hooks. No transcripts, prompts or arguments persist.
    if (changed || emit || now - (previous.observedEventAt ?? 0) >= 60000) {
      const receipt = { key, emittedAt: emit ? now : (previous.emittedAt ?? 0), observedEventAt: now,
        level: observation.level, event: event.hook_event_name };
      const tmp = `${ledgerFile}.${process.pid}.tmp`;
      fs.writeFileSync(tmp, JSON.stringify(receipt), { flag: 'wx', mode: 0o600 });
      fs.renameSync(tmp, ledgerFile);
    }
    return emit ? { hookSpecificOutput: { hookEventName: event.hook_event_name,
      additionalContext: observation.message } } : {};
  } finally { fs.closeSync(fd); fs.unlinkSync(lock); }
}

export async function main(args = process.argv.slice(2)) {
  const guardDir = path.join(process.env.LOCALAPPDATA ?? '', 'OpenAI', 'CodexUsageGuard');
  // Small durable receipts belong on D, never in project source or user profiles.
  const ledgerDir = 'D:\\Codex\\State\\UsageGuard\\agent-alerts';
  const observation = readObservation(guardDir);
  if (args.length === 1 && args[0] === '--check') {
    let delivery = 'unverified_use_checkpoint_fallback';
    const session = process.env.CODEX_THREAD_ID;
    if (session) {
      try {
        const id = crypto.createHash('sha256').update(session).digest('hex');
        const receipt = readJson(path.join(ledgerDir, `${id}.json`));
        const age = Date.now() - receipt.observedEventAt;
        if (age >= 0 && age < 120000) delivery = 'hook_event_observed_recently';
      } catch { /* No observed hook, so keep the fallback. */ }
    }
    process.stdout.write(JSON.stringify({ ...observation, source: 'helper_cached_decision',
      delivery, liveQuotaRequest: false }) + '\n');
    return;
  }
  if (args.length) throw new Error('unsupported arguments');
  let input = '';
  for await (const chunk of process.stdin) {
    input += chunk;
    if (Buffer.byteLength(input) > 4 * 1024 * 1024) throw new Error('input too large');
  }
  const event = JSON.parse(input);
  try { process.stdout.write(JSON.stringify(deliver(event, observation, ledgerDir)) + '\n'); }
  catch {
    // Failure is visible but never blocks a tool or prevents cleanup.
    process.stdout.write(JSON.stringify(EVENTS.has(event.hook_event_name) ? {
      hookSpecificOutput: { hookEventName: event.hook_event_name,
        additionalContext: unknown('alert receiver cannot save its receipt').message }
    } : {}) + '\n');
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(() => { process.stderr.write('Usage Guard alert receiver failed; use the cached checkpoint fallback.\n'); process.exitCode = 1; });
}
