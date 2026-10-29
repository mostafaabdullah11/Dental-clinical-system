// Frontend security lint (SECURITY_DESIGN.md §12, §13; DEPLOYMENT_ARCHITECTURE.md §2.2 "frontend" job).
// Dependency-free so it runs anywhere Node runs. Fails the build on:
//   - dangerouslySetInnerHTML, innerHTML / outerHTML / insertAdjacentHTML / document.write (raw HTML sinks: XSS)
//   - eval / new Function / string timers (code from strings)
//   - localStorage / sessionStorage / indexedDB (no data or tokens in browser storage)
//   - window.alert / prompt / confirm (UI uses in-page Arabic feedback instead)
// A reviewed exception must be written on the same line as:  // lint-allow: <rule> — <reason>
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../src/', import.meta.url));
const rules = [
  { id: 'no-dangerously-set-inner-html', pattern: /dangerouslySetInnerHTML/ },
  { id: 'no-raw-html-sink', pattern: /\.(innerHTML|outerHTML)\s*=|insertAdjacentHTML\s*\(|document\.write\s*\(/ },
  { id: 'no-eval', pattern: /\beval\s*\(|new\s+Function\s*\(|set(Timeout|Interval)\s*\(\s*['"`]/ },
  { id: 'no-browser-storage', pattern: /\b(localStorage|sessionStorage|indexedDB)\b/ },
  { id: 'no-native-dialogs', pattern: /\b(window\.)?(alert|prompt|confirm)\s*\(/ },
];

function files(dir) {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? files(path) : /\.(tsx?|jsx?)$/.test(name) ? [path] : [];
  });
}

const problems = [];
for (const file of files(root)) {
  const lines = readFileSync(file, 'utf8').split(/\r?\n/);
  lines.forEach((line, index) => {
    const code = line.replace(/\/\/.*$/, (comment) => (comment.includes('lint-allow:') ? comment : ''));
    for (const rule of rules) {
      if (rule.pattern.test(code) && !line.includes(`lint-allow: ${rule.id}`)) {
        problems.push(`${relative(root, file)}:${index + 1}  ${rule.id}  ${line.trim()}`);
      }
    }
  });
}

if (problems.length > 0) {
  console.error(`Security lint failed (${problems.length}):\n${problems.join('\n')}`);
  process.exit(1);
}
console.log(`Security lint passed (${files(root).length} files, ${rules.length} rules).`);
