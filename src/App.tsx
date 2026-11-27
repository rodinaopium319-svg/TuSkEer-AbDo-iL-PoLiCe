import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Check,
  Copy,
  Download,
  FileCode2,
  FolderOpen,
  Pause,
  Play,
  Search,
  Shield,
  Square,
  Zap,
} from 'lucide-react';

interface ProjectFileEntry {
  relativePath: string;
  sizeBytes: number;
  isBinary: boolean;
  lineCount: number;
}

interface SimScript {
  id: number;
  name: string;
  hotkey: string;
  content: string;
}

const DEFAULT_SANDBOX_SCRIPTS: SimScript[] = [
  {
    id: 1,
    name: 'Script VIP',
    hotkey: 'F1',
    content:
      'TuSkEer AbDo iL PoLiCe ❤️ مرحبا بك في برنامج\nأسرع برنامج كتابة تلقائية على ويندوز\nههههه مع أفضل أداء وتصميم عصري 🔥',
  },
  {
    id: 2,
    name: 'Priority Interrupt B',
    hotkey: 'F6',
    content:
      'Priority alert message!\nPreempting cleanly at sentence boundary and resuming Script A automatically.',
  },
];

function segmentGraphemes(text: string): string[] {
  if (typeof Intl !== 'undefined' && 'Segmenter' in Intl) {
    const seg = new Intl.Segmenter('en', { granularity: 'grapheme' });
    return Array.from(seg.segment(text), (s) => s.segment);
  }
  return Array.from(text);
}

export default function App() {
  const [files, setFiles] = useState<ProjectFileEntry[]>([]);
  const [filesLoading, setFilesLoading] = useState(true);
  const [selectedPath, setSelectedPath] = useState<string>('src-wpf/TuSkEer.AbDoILPoLiCe/Services/TypingEngine.cs');
  const [fileContent, setFileContent] = useState<string>('');
  const [contentLoading, setContentLoading] = useState(false);
  const [fileSearch, setFileSearch] = useState('');
  const [copiedPath, setCopiedPath] = useState<string | null>(null);

  // Interactive Sandbox Verification of TypingEngine + Priority Queue behavior
  const [sandboxDelayMs, setSandboxDelayMs] = useState<number>(30);
  const [repeatFastSend, setRepeatFastSend] = useState<boolean>(true);
  const [repeatThreshold, setRepeatThreshold] = useState<number>(5);
  const [appendEnabled, setAppendEnabled] = useState<boolean>(true);
  const [appendText, setAppendText] = useState<string>(' 🔥❤️');
  const [sandboxOutput, setSandboxOutput] = useState<string[]>([]);
  const [activeLineBuffer, setActiveLineBuffer] = useState<string>('');
  const [engineStatus, setEngineStatus] = useState<'Ready' | 'Typing' | 'Pausing' | 'Paused' | 'Switching'>('Ready');
  const [activeScriptName, setActiveScriptName] = useState<string>('None');
  const [queueSummary, setQueueSummary] = useState<string>('Empty');

  const simRef = useRef<{
    stopped: boolean;
    pauseAfterSentence: boolean;
    paused: boolean;
    preemptPending: SimScript | null;
    resumeWaiter: (() => void) | null;
    delayMs: number;
    repeatEnabled: boolean;
    threshold: number;
    appendEnabled: boolean;
    appendText: string;
  }>({
    stopped: true,
    pauseAfterSentence: false,
    paused: false,
    preemptPending: null,
    resumeWaiter: null,
    delayMs: 30,
    repeatEnabled: true,
    threshold: 5,
    appendEnabled: true,
    appendText: ' 🔥❤️',
  });

  useEffect(() => {
    simRef.current.delayMs = sandboxDelayMs;
    simRef.current.repeatEnabled = repeatFastSend;
    simRef.current.threshold = repeatThreshold;
    simRef.current.appendEnabled = appendEnabled;
    simRef.current.appendText = appendText;
  }, [sandboxDelayMs, repeatFastSend, repeatThreshold, appendEnabled, appendText]);

  useEffect(() => {
    let active = true;
    setFilesLoading(true);
    fetch('/api/project/files')
      .then((res) => res.json())
      .then((data) => {
        if (!active) return;
        const list: ProjectFileEntry[] = data.files ?? [];
        setFiles(list);
        if (list.length > 0 && !list.some((f) => f.relativePath === selectedPath)) {
          setSelectedPath(list[0].relativePath);
        }
      })
      .finally(() => {
        if (active) setFilesLoading(false);
      });
    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    if (!selectedPath) return;
    let active = true;
    setContentLoading(true);
    fetch(`/api/project/file?path=${encodeURIComponent(selectedPath)}`)
      .then((res) => res.json())
      .then((data) => {
        if (!active) return;
        setFileContent(data.isBinary ? '[Binary asset included in downloadable ZIP]' : data.content ?? '');
      })
      .finally(() => {
        if (active) setContentLoading(false);
      });
    return () => {
      active = false;
    };
  }, [selectedPath]);

  const filteredFiles = useMemo(() => {
    const q = fileSearch.trim().toLowerCase();
    if (!q) return files;
    return files.filter((f) => f.relativePath.toLowerCase().includes(q));
  }, [files, fileSearch]);

  const totalSourceLines = useMemo(
    () => files.reduce((sum, f) => sum + f.lineCount, 0),
    [files]
  );

  const handleCopyFile = async () => {
    if (!fileContent) return;
    await navigator.clipboard.writeText(fileContent);
    setCopiedPath(selectedPath);
    setTimeout(() => setCopiedPath(null), 1600);
  };

  const takePreemptPending = (): SimScript | null => {
    const s = simRef.current.preemptPending;
    simRef.current.preemptPending = null;
    return s;
  };

  const runSandboxScript = async (script: SimScript) => {
    if (engineStatus === 'Typing' || engineStatus === 'Pausing') {
      simRef.current.preemptPending = script;
      setEngineStatus('Switching');
      setQueueSummary(`Finishing sentence, then switching to ${script.name}`);
      return;
    }

    simRef.current.stopped = false;
    simRef.current.pauseAfterSentence = false;
    simRef.current.paused = false;
    simRef.current.preemptPending = null;

    const stack: { script: SimScript; nextIndex: number; lines: string[] }[] = [
      {
        script,
        nextIndex: 0,
        lines: script.content.split(/\r?\n/).filter((l) => l.trim().length > 0),
      },
    ];

    while (stack.length > 0 && !simRef.current.stopped) {
      const current = stack[stack.length - 1];
      if (current.nextIndex >= current.lines.length) {
        stack.pop();
        continue;
      }

      setActiveScriptName(current.script.name);
      setEngineStatus('Typing');
      setQueueSummary(
        stack.length > 1
          ? `Active: ${current.script.name} · Suspended: ${stack[0].script.name} (sentence ${stack[0].nextIndex + 1})`
          : `Running ${current.script.name} (${current.nextIndex + 1}/${current.lines.length})`
      );

      const sentence = current.lines[current.nextIndex];
      const elements = segmentGraphemes(sentence);
      let buffer = '';
      let idx = 0;

      while (idx < elements.length && !simRef.current.stopped) {
        if (simRef.current.repeatEnabled) {
          const ch = elements[idx];
          let runEnd = idx + 1;
          while (runEnd < elements.length && elements[runEnd] === ch) runEnd++;
          if (runEnd - idx >= simRef.current.threshold) {
            buffer += elements.slice(idx, runEnd).join('');
            setActiveLineBuffer(buffer);
            idx = runEnd;
            if (simRef.current.delayMs > 0) {
              await new Promise((r) => setTimeout(r, simRef.current.delayMs));
            }
            continue;
          }
        }

        buffer += elements[idx];
        setActiveLineBuffer(buffer);
        idx++;
        if (simRef.current.delayMs > 0 && idx < elements.length) {
          await new Promise((r) => setTimeout(r, simRef.current.delayMs));
        }
      }

      if (simRef.current.stopped) break;

      if (simRef.current.appendEnabled && simRef.current.appendText) {
        buffer += simRef.current.appendText;
      }

      setSandboxOutput((prev) => [...prev.slice(-10), buffer]);
      setActiveLineBuffer('');
      current.nextIndex++;

      const nextScript = takePreemptPending();
      if (nextScript) {
        if (current.nextIndex >= current.lines.length) {
          stack.pop();
        }
        stack.push({
          script: nextScript,
          nextIndex: 0,
          lines: nextScript.content.split(/\r?\n/).filter((l: string) => l.trim().length > 0),
        });
        continue;
      }

      if (simRef.current.pauseAfterSentence) {
        simRef.current.pauseAfterSentence = false;
        simRef.current.paused = true;
        setEngineStatus('Paused');
        setQueueSummary(`Paused ${current.script.name} at sentence ${current.nextIndex + 1}/${current.lines.length}`);
        await new Promise<void>((resolve) => {
          simRef.current.resumeWaiter = resolve;
        });
      }
    }

    if (!simRef.current.stopped) {
      setEngineStatus('Ready');
      setActiveScriptName('None');
      setQueueSummary('Completed');
    }
  };

  const toggleSandboxPause = () => {
    if (engineStatus === 'Paused' && simRef.current.resumeWaiter) {
      simRef.current.paused = false;
      const w = simRef.current.resumeWaiter;
      simRef.current.resumeWaiter = null;
      w();
    } else if (engineStatus === 'Typing') {
      simRef.current.pauseAfterSentence = true;
      setEngineStatus('Pausing');
    }
  };

  const stopSandbox = () => {
    simRef.current.stopped = true;
    simRef.current.pauseAfterSentence = false;
    simRef.current.preemptPending = null;
    if (simRef.current.resumeWaiter) {
      const w = simRef.current.resumeWaiter;
      simRef.current.resumeWaiter = null;
      w();
    }
    setActiveLineBuffer('');
    setEngineStatus('Ready');
    setActiveScriptName('None');
    setQueueSummary('Stopped and queue cleared');
  };

  return (
    <div className="min-h-screen bg-[#060B18] text-slate-100">
      {/* Top Bar Contract: Brand wordmark — 4 nav links — 1 primary CTA */}
      <header className="sticky top-0 z-30 border-b border-slate-800/80 bg-[#060B18]/90 backdrop-blur-md">
        <div className="mx-auto flex max-w-[1400px] items-center justify-between gap-8 px-6 py-4">
          <a href="#overview" className="font-display text-lg font-bold tracking-tight text-white whitespace-nowrap shrink-0">
            TuSkEer AbDo iL PoLiCe
          </a>

          <nav className="hidden md:flex items-center gap-6 text-sm font-medium text-slate-400">
            <a href="#overview" className="hover:text-white transition-colors whitespace-nowrap shrink-0">Overview</a>
            <a href="#sandbox" className="hover:text-white transition-colors whitespace-nowrap shrink-0">Engine Logic</a>
            <a href="#source" className="hover:text-white transition-colors whitespace-nowrap shrink-0">WPF Source</a>
            <a href="#build" className="hover:text-white transition-colors whitespace-nowrap shrink-0">Windows Build</a>
          </nav>

          <a
            href="/api/project/download"
            className="inline-flex items-center gap-2 rounded-lg bg-sky-500 px-4 py-2 text-xs font-semibold text-slate-950 hover:bg-sky-400 transition-colors whitespace-nowrap shrink-0"
          >
            <Download className="h-3.5 w-3.5" />
            Download WPF Solution
          </a>
        </div>
      </header>

      <main className="mx-auto max-w-[1400px] px-6 py-10 space-y-14">
        {/* Hero Overview */}
        <section id="overview" className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-stretch">
          <div className="lg:col-span-7 flex flex-col justify-between rounded-2xl border border-slate-800 bg-[#0B1328] p-8">
            <div>
              <p className="text-xs font-medium text-sky-400 mb-3">
                Native Windows 10 &amp; 11 Desktop Project · C# / .NET 10 / WPF / SQLite
              </p>
              <h1 className="font-display text-3xl sm:text-4xl font-bold tracking-tight text-white [text-wrap:balance]">
                High-Precision Windows Auto Typing Engine &amp; Script Manager
              </h1>
              <p className="mt-4 text-sm sm:text-base leading-relaxed text-slate-300 max-w-2xl">
                This workspace contains the complete native <span className="text-white font-medium">.NET 10 WPF</span> Windows application source code, Win32 <span className="font-mono text-sky-300">SendInput</span> and <span className="font-mono text-sky-300">WH_KEYBOARD_LL</span> hook engine, SQLite WAL persistence, and Windows x64 GitHub Actions release pipeline. Because this sandbox runs Linux without the Windows Desktop SDK, you can inspect every source file, dry-run the scheduling logic below, and download the complete Windows Visual Studio solution as a ZIP.
              </p>
            </div>

            <div className="mt-8 grid grid-cols-2 sm:grid-cols-4 gap-4 border-t border-slate-800 pt-6">
              <div>
                <div className="text-xs text-slate-400">WPF Project Files</div>
                <div className="mt-1 font-mono text-xl font-semibold text-white tabular-nums">{files.length}</div>
              </div>
              <div>
                <div className="text-xs text-slate-400">C# &amp; XAML Lines</div>
                <div className="mt-1 font-mono text-xl font-semibold text-sky-400 tabular-nums">{totalSourceLines}</div>
              </div>
              <div>
                <div className="text-xs text-slate-400">Target Runtime</div>
                <div className="mt-1 font-mono text-xl font-semibold text-white">net10.0-win</div>
              </div>
              <div>
                <div className="text-xs text-slate-400">Speed Range</div>
                <div className="mt-1 font-mono text-xl font-semibold text-rose-400 tabular-nums">0–1000 ms</div>
              </div>
            </div>
          </div>

          <div className="lg:col-span-5 rounded-2xl border border-slate-800 bg-[#0B1328] p-6 flex flex-col justify-between gap-4">
            <div className="flex items-center gap-4">
              <img
                src="/assets/aistudio/286accc7571120a9087aa6dcf24d3207_539f3b5a.jpg"
                alt="Project owner portrait"
                referrerPolicy="no-referrer"
                className="h-16 w-16 rounded-full object-cover border-2 border-sky-400/70 shrink-0"
              />
              <div>
                <h2 className="font-display text-lg font-semibold text-white">Original Branding &amp; UI Reference</h2>
                <p className="text-xs text-slate-400">
                  Embedded as <span className="font-mono text-slate-300">owner_portrait.jpg</span> and multi-size <span className="font-mono text-slate-300">app_icon.ico</span> inside WPF Resources.
                </p>
              </div>
            </div>

            <div className="overflow-hidden rounded-xl border border-slate-800 bg-[#060B18]">
              <img
                src="/assets/aistudio/file_000000005924820a91021ea488e827ea_f8d02dbb.png"
                alt="TuSkEer AbDo iL PoLiCe futuristic WPF layout reference"
                referrerPolicy="no-referrer"
                className="w-full h-56 object-cover"
              />
            </div>

            <div className="flex items-center justify-between text-xs text-slate-400">
              <span>WDA_EXCLUDEFROMCAPTURE · NotifyIcon Tray</span>
              <span>GitHub Actions windows-latest</span>
            </div>
          </div>
        </section>

        {/* Interactive Logic Sandbox (Browser Simulation of C# TypingEngine + Scheduler) */}
        <section id="sandbox" className="rounded-2xl border border-slate-800 bg-[#0B1328] p-6 sm:p-8">
          <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 mb-6">
            <div>
              <h2 className="font-display text-xl font-semibold text-white">Engine &amp; Priority Queue Logic Verifier</h2>
              <p className="text-xs text-slate-400 mt-1">
                Interactive browser dry-run matching the C# <span className="font-mono text-slate-300">TypingEngine</span> and <span className="font-mono text-slate-300">ScriptQueueScheduler</span> rules (sentence-boundary pause, repeat fast-send, append content, and Script B preemption).
              </p>
            </div>
            <div className="text-xs font-mono text-sky-400 tabular-nums">
              State: {engineStatus} · Script: {activeScriptName}
            </div>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
            <div className="lg:col-span-5 space-y-4">
              <div className="space-y-2">
                <div className="flex justify-between text-xs text-slate-300">
                  <span>Typing Delay (0–1000 ms)</span>
                  <span className="font-mono text-sky-400 tabular-nums">{sandboxDelayMs} ms</span>
                </div>
                <input
                  type="range"
                  min={0}
                  max={300}
                  value={sandboxDelayMs}
                  onChange={(e) => setSandboxDelayMs(Number(e.target.value))}
                  className="w-full accent-sky-400 cursor-pointer"
                />
              </div>

              <div className="flex items-center justify-between gap-4 border-t border-slate-800 pt-3">
                <label className="flex items-center gap-2 text-xs text-slate-200 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={repeatFastSend}
                    onChange={(e) => setRepeatFastSend(e.target.checked)}
                    className="accent-sky-400"
                  />
                  Repeat Characters Fast Send (e.g. ههههه)
                </label>
                <input
                  type="number"
                  min={2}
                  max={20}
                  value={repeatThreshold}
                  onChange={(e) => setRepeatThreshold(Math.max(2, Number(e.target.value) || 5))}
                  aria-label="Repeat threshold"
                  className="w-16 rounded border border-slate-700 bg-[#060B18] px-2 py-1 text-xs font-mono text-white text-center tabular-nums"
                />
              </div>

              <div className="space-y-2 border-t border-slate-800 pt-3">
                <label className="flex items-center gap-2 text-xs text-slate-200 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={appendEnabled}
                    onChange={(e) => setAppendEnabled(e.target.checked)}
                    className="accent-sky-400"
                  />
                  APPEND CONTENT
                </label>
                <input
                  type="text"
                  value={appendText}
                  onChange={(e) => setAppendText(e.target.value)}
                  className="w-full rounded-lg border border-slate-700 bg-[#060B18] px-3 py-2 text-xs text-white"
                />
              </div>

              <div className="flex flex-wrap gap-2 pt-2">
                <button
                  onClick={() => runSandboxScript(DEFAULT_SANDBOX_SCRIPTS[0])}
                  className="inline-flex items-center gap-1.5 rounded-lg bg-sky-500 px-3.5 py-2 text-xs font-semibold text-slate-950 hover:bg-sky-400 transition-colors cursor-pointer"
                >
                  <Play className="h-3.5 w-3.5" />
                  Run Script A (F1)
                </button>
                <button
                  onClick={() => runSandboxScript(DEFAULT_SANDBOX_SCRIPTS[1])}
                  className="inline-flex items-center gap-1.5 rounded-lg border border-sky-500/50 bg-sky-500/10 px-3.5 py-2 text-xs font-semibold text-sky-300 hover:bg-sky-500/20 transition-colors cursor-pointer"
                >
                  <Zap className="h-3.5 w-3.5" />
                  Interrupt with Script B (F6)
                </button>
                <button
                  onClick={toggleSandboxPause}
                  className="inline-flex items-center gap-1.5 rounded-lg border border-violet-500/50 bg-violet-500/15 px-3.5 py-2 text-xs font-semibold text-violet-200 hover:bg-violet-500/25 transition-colors cursor-pointer"
                >
                  <Pause className="h-3.5 w-3.5" />
                  {engineStatus === 'Paused' ? 'Resume' : 'Pause'}
                </button>
                <button
                  onClick={stopSandbox}
                  className="inline-flex items-center gap-1.5 rounded-lg border border-rose-500/50 bg-rose-500/15 px-3.5 py-2 text-xs font-semibold text-rose-200 hover:bg-rose-500/25 transition-colors cursor-pointer"
                >
                  <Square className="h-3.5 w-3.5" />
                  Stop
                </button>
              </div>
            </div>

            <div className="lg:col-span-7 rounded-xl border border-slate-800 bg-[#060B18] p-4 flex flex-col justify-between min-h-[240px]">
              <div className="flex items-center justify-between text-xs text-slate-400 border-b border-slate-800 pb-2 mb-3">
                <span>Simulated Foreground Target Output</span>
                <span className="font-mono text-violet-300">{queueSummary}</span>
              </div>

              <div className="flex-1 space-y-1.5 font-mono text-xs overflow-y-auto max-h-48">
                {sandboxOutput.length === 0 && !activeLineBuffer && (
                  <div className="text-slate-500">
                    Click &ldquo;Run Script A (F1)&rdquo;, then click &ldquo;Interrupt with Script B (F6)&rdquo; mid-run to verify sentence-boundary suspension and automatic resumption.
                  </div>
                )}
                {sandboxOutput.map((line, i) => (
                  <div key={i} className="text-emerald-300" dir="auto">
                    {line}
                  </div>
                ))}
                {activeLineBuffer && (
                  <div className="text-sky-300" dir="auto">
                    {activeLineBuffer}
                    <span className="inline-block w-1.5 h-3.5 bg-sky-400 ml-0.5 align-middle" />
                  </div>
                )}
              </div>

              {sandboxOutput.length > 0 && (
                <div className="pt-3 border-t border-slate-800 flex justify-end">
                  <button
                    onClick={() => setSandboxOutput([])}
                    className="text-xs text-slate-400 hover:text-white transition-colors cursor-pointer"
                  >
                    Clear Output
                  </button>
                </div>
              )}
            </div>
          </div>
        </section>

        {/* Complete C# / XAML Source Explorer */}
        <section id="source" className="rounded-2xl border border-slate-800 bg-[#0B1328] p-6 sm:p-8">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6">
            <div>
              <h2 className="font-display text-xl font-semibold text-white">WPF Solution Source Files</h2>
              <p className="text-xs text-slate-400 mt-1">
                Inspect every C#, XAML, SQLite, Win32 P/Invoke, and GitHub Actions file generated in this repository.
              </p>
            </div>
            <div className="relative w-full sm:w-72">
              <Search className="h-4 w-4 text-slate-400 absolute left-3 top-2.5" />
              <input
                type="text"
                placeholder="Filter project files..."
                value={fileSearch}
                onChange={(e) => setFileSearch(e.target.value)}
                className="w-full rounded-lg border border-slate-700 bg-[#060B18] pl-9 pr-3 py-2 text-xs text-white placeholder:text-slate-500"
              />
            </div>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
            <div className="lg:col-span-4 rounded-xl border border-slate-800 bg-[#060B18] p-3 max-h-[520px] overflow-y-auto space-y-1">
              {filesLoading ? (
                <div className="p-4 text-xs text-slate-400">Loading project files...</div>
              ) : filteredFiles.length === 0 ? (
                <div className="p-4 text-xs text-slate-400">No files match &ldquo;{fileSearch}&rdquo;.</div>
              ) : (
                filteredFiles.map((file) => {
                  const isSelected = file.relativePath === selectedPath;
                  return (
                    <button
                      key={file.relativePath}
                      onClick={() => setSelectedPath(file.relativePath)}
                      className={`w-full text-left rounded-lg px-3 py-2 text-xs transition-colors flex items-center justify-between gap-2 cursor-pointer ${
                        isSelected
                          ? 'bg-sky-500/15 text-sky-300 border border-sky-500/40'
                          : 'text-slate-300 hover:bg-slate-800/60 border border-transparent'
                      }`}
                    >
                      <span className="flex items-center gap-2 truncate">
                        <FileCode2 className="h-3.5 w-3.5 shrink-0 text-slate-400" />
                        <span className="truncate font-mono">{file.relativePath}</span>
                      </span>
                      <span className="text-[11px] font-mono text-slate-500 shrink-0 tabular-nums">
                        {file.isBinary ? `${Math.round(file.sizeBytes / 1024)} KB` : `${file.lineCount}L`}
                      </span>
                    </button>
                  );
                })
              )}
            </div>

            <div className="lg:col-span-8 rounded-xl border border-slate-800 bg-[#060B18] flex flex-col max-h-[520px]">
              <div className="flex items-center justify-between border-b border-slate-800 px-4 py-3">
                <div className="flex items-center gap-2 text-xs font-mono text-slate-300 truncate">
                  <FolderOpen className="h-4 w-4 text-sky-400 shrink-0" />
                  <span className="truncate">{selectedPath}</span>
                </div>
                <button
                  onClick={handleCopyFile}
                  disabled={!fileContent}
                  className="inline-flex items-center gap-1.5 rounded-md border border-slate-700 px-2.5 py-1 text-xs text-slate-200 hover:border-sky-400 transition-colors cursor-pointer shrink-0"
                >
                  {copiedPath === selectedPath ? (
                    <>
                      <Check className="h-3.5 w-3.5 text-emerald-400" />
                      Copied
                    </>
                  ) : (
                    <>
                      <Copy className="h-3.5 w-3.5" />
                      Copy
                    </>
                  )}
                </button>
              </div>
              <pre className="flex-1 overflow-auto p-4 text-xs leading-relaxed text-slate-200 font-mono">
                {contentLoading ? 'Loading file contents...' : fileContent}
              </pre>
            </div>
          </div>
        </section>

        {/* Windows Build & Honest Limitations */}
        <section id="build" className="grid grid-cols-1 md:grid-cols-2 gap-6">
          <div className="rounded-2xl border border-slate-800 bg-[#0B1328] p-6">
            <h2 className="font-display text-lg font-semibold text-white mb-3">Windows x64 Release Build</h2>
            <p className="text-xs text-slate-300 leading-relaxed mb-4">
              Build locally on Windows 10/11 with the .NET 10 SDK or push to GitHub to run <span className="font-mono text-sky-300">.github/workflows/build-windows.yml</span> on <span className="font-mono text-sky-300">windows-latest</span>, producing a self-contained <span className="font-mono text-white">win-x64</span> ZIP without requiring users to install .NET separately.
            </p>
            <pre className="rounded-xl border border-slate-800 bg-[#060B18] p-4 text-xs font-mono text-sky-200 overflow-x-auto">
{`dotnet restore TuSkEerAbDoILPoLiCe.sln
dotnet build TuSkEerAbDoILPoLiCe.sln -c Release --no-restore
dotnet publish src-wpf/TuSkEer.AbDoILPoLiCe/TuSkEer.AbDoILPoLiCe.csproj -c Release -r win-x64 --self-contained true`}
            </pre>
          </div>

          <div className="rounded-2xl border border-slate-800 bg-[#0B1328] p-6">
            <div className="flex items-center gap-2 mb-3">
              <Shield className="h-4 w-4 text-amber-400" />
              <h2 className="font-display text-lg font-semibold text-white">Verified Scope &amp; Platform Notes</h2>
            </div>
            <ul className="space-y-2 text-xs text-slate-300 leading-relaxed">
              <li>• <span className="text-white font-medium">Linux Container Limitation</span>: Because this AI Studio runtime runs Linux without the Windows Desktop WPF SDK, compiling the <span className="font-mono">.exe</span> and running Win32 hook/UI tests must execute on Windows or GitHub Actions <span className="font-mono">windows-latest</span>.</li>
              <li>• <span className="text-white font-medium">Screen Capture Exclusion</span>: Uses <span className="font-mono">SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)</span>, supported on Windows 10 2004+ and Windows 11 for standard capture APIs; kernel or legacy capture drivers may still capture the window.</li>
              <li>• <span className="text-white font-medium">UIPI Elevation Boundary</span>: Sending keystrokes into an elevated (Administrator) target window requires launching TuSkEer AbDo iL PoLiCe with matching elevation.</li>
            </ul>
          </div>
        </section>
      </main>
    </div>
  );
}
