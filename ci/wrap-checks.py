# -*- coding: utf-8 -*-
# Puts every check of hers inside #if CHECKS, so the build that ships has none and the test build all. A check is a
# method named Smoke..., one taking a list of checks, one in EXTRA, or a whole *Checks.cs file. Run twice, no change.
#
#     python -I ci/wrap-checks.py [app]
import os, re, sys

here = os.path.dirname(os.path.abspath(__file__))
app = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, "..", "app")

HEADER = re.compile(r"(?m)^(?P<indent>[ \t]*)(?:\[[^\]]*\][ \t]*)*(?:(?:public|private|internal|protected|static|override|virtual|unsafe|sealed|new|partial|async|extern)\s+)+(?:[\w<>\[\],.?]+(?:\s*<[^>]*>)?\s+)(?P<name>\w+)\s*(?:<[^>]*>)?\s*\(")
# Helpers only the checks call, by name or as File.cs:Name where the name is common.
EXTRA = {'StartSmokeTest', 'WindowQuiet', 'SoundsTips', 'DropdownTextAt', 'SmokeRun.cs:Run', 'SmokeRun.cs:RunLocked', 'Onboarding.cs:Check', 'MainframeKeys.cs:CheckMarkPictures'}
CHECKS_PARAM = re.compile(r"List\s*<\s*string\s*>\s+checks\b")


def scan_to_body_end(text, start):
    # From start (the method's header), find its opening brace and the matching close, skipping strings, chars and
    # comments. Returns the index just past the closing brace, or -1 for a header with no body (abstract, extern, =>).
    i, n, depth, opened = start, len(text), 0, False
    while i < n:
        c = text[i]
        if c == '/' and i + 1 < n and text[i + 1] == '/':
            j = text.find('\n', i)
            i = n if j < 0 else j
            continue
        if c == '/' and i + 1 < n and text[i + 1] == '*':
            j = text.find('*/', i + 2)
            i = n if j < 0 else j + 2
            continue
        if c == '@' and i + 1 < n and text[i + 1] == '"':
            i += 2
            while i < n:
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            continue
        if c == '"':
            i += 1
            while i < n and text[i] != '"':
                i += 2 if text[i] == '\\' else 1
            i += 1
            continue
        if c == "'":
            i += 1
            while i < n and text[i] != "'":
                i += 2 if text[i] == '\\' else 1
            i += 1
            continue
        if not opened and c == ';' and depth == 0:
            return -1
        if not opened and c == '=' and i + 1 < n and text[i + 1] == '>' and depth == 0:
            return -1
        if c == '{':
            depth += 1
            opened = True
        elif c == '}':
            depth -= 1
            if opened and depth == 0:
                return i + 1
        i += 1
    return -1


def inside_checks(text, at):
    # Whether at already sits inside an open #if CHECKS.
    depth = 0
    for line in text[:at].split('\n'):
        s = line.strip()
        if s.startswith('#if CHECKS'):
            depth += 1
        elif s.startswith('#endif') and depth > 0:
            depth -= 1
    return depth > 0


def wrap_file(path):
    raw = open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    crlf = '\r\n' in text
    text = text.replace('\r\n', '\n')
    name = os.path.basename(path)
    changed = 0
    if name.endswith('Checks.cs'):
        if '#if CHECKS' not in text:
            lines = text.split('\n')
            first = next(i for i, l in enumerate(lines) if l.strip() and not l.strip().startswith('using ') and not l.strip().startswith('//'))
            text = '\n'.join(lines[:first] + ['#if CHECKS'] + lines[first:]).rstrip('\n') + '\n#endif\n'
            changed = 1
    else:
        spans = []
        for m in HEADER.finditer(text, 0) if False else []:
            pass
        pos = 0
        lines_at = [0]
        for m in re.finditer(r'\n', text):
            lines_at.append(m.end())
        for line_start in lines_at:
            m = HEADER.match(text, line_start)
            if not m:
                continue
            method = m.group('name')
            paren = text.find('(', m.end() - 1)
            close = text.find(')', paren)
            params = text[paren:close + 1] if close > 0 else ''
            if not (method.startswith('Smoke') or method in EXTRA or (name + ':' + method) in EXTRA or CHECKS_PARAM.search(params)):
                continue
            end = scan_to_body_end(text, m.start())
            if end < 0:
                continue
            spans.append((line_start, end, method))
        # Outermost only, and not already wrapped.
        kept = []
        for s, e, nm in spans:
            if any(s > ks and e <= ke for ks, ke, _ in spans if (ks, ke) != (s, e)):
                continue
            if inside_checks(text, s):
                continue
            kept.append((s, e, nm))
        for s, e, nm in sorted(kept, reverse=True):
            line_end = text.find('\n', e)
            line_end = len(text) if line_end < 0 else line_end
            text = text[:s] + '#if CHECKS\n' + text[s:line_end] + '\n#endif' + text[line_end:]
            changed += 1
    if changed:
        if crlf:
            text = text.replace('\n', '\r\n')
        open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + text.encode('utf-8'))
    return changed


total = 0
for name in sorted(os.listdir(app)):
    if not name.endswith('.cs') or name.endswith('.local.cs'):
        continue
    made = wrap_file(os.path.join(app, name))
    if made:
        total += made
        print('%-28s %d' % (name, made))
print('%d wrapped' % total)
