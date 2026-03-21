#!/usr/bin/env python3
"""
Collapse multi-line C# statements to single lines in HaleyStorage project.
"""
import os
import re

SOURCE_ROOT = r"E:/HaleyProject/HaleyStorage/HaleyStorage"

def get_cs_files(root):
    files = []
    for dirpath, dirnames, filenames in os.walk(root):
        # Skip obj directories
        dirnames[:] = [d for d in dirnames if d != 'obj']
        for f in filenames:
            if f.endswith('.cs'):
                files.append(os.path.join(dirpath, f))
    return files


def count_open_parens(s):
    """Count net open parentheses/brackets in a string, ignoring string literals."""
    count = 0
    in_str = False
    str_char = None
    in_verbatim = False
    j = 0
    while j < len(s):
        c = s[j]
        if in_verbatim:
            if c == '"':
                if j + 1 < len(s) and s[j+1] == '"':
                    j += 2
                    continue
                else:
                    in_verbatim = False
        elif in_str:
            if c == '\\':
                j += 2
                continue
            elif c == str_char:
                in_str = False
        else:
            if c == '@' and j + 1 < len(s) and s[j+1] == '"':
                in_verbatim = True
                j += 2
                continue
            elif c in ('"', "'"):
                in_str = True
                str_char = c
            elif c in ('(', '['):
                count += 1
            elif c in (')', ']'):
                count -= 1
        j += 1
    return count


def count_open_braces(s):
    """Count net open curly braces, ignoring string literals."""
    count = 0
    in_str = False
    str_char = None
    in_verbatim = False
    j = 0
    while j < len(s):
        c = s[j]
        if in_verbatim:
            if c == '"':
                if j + 1 < len(s) and s[j+1] == '"':
                    j += 2
                    continue
                else:
                    in_verbatim = False
        elif in_str:
            if c == '\\':
                j += 2
                continue
            elif c == str_char:
                in_str = False
        else:
            if c == '@' and j + 1 < len(s) and s[j+1] == '"':
                in_verbatim = True
                j += 2
                continue
            elif c in ('"', "'"):
                in_str = True
                str_char = c
            elif c == '{':
                count += 1
            elif c == '}':
                count -= 1
        j += 1
    return count


def is_string_continuation(line):
    """Check if a line contains SQL-like string concatenation or verbatim string."""
    stripped = line.strip()
    # Lines that are just string literals (SQL content)
    if stripped.startswith('@"') or stripped.startswith('"') or stripped.startswith('$@"') or stripped.startswith('$"'):
        return True
    # Lines that start with + and then a string (SQL concatenation)
    if re.match(r'^\+\s*[@$]?"', stripped):
        return True
    return False


def is_block_opener(stripped):
    """Check if stripped line is a control flow / declaration that opens a block."""
    # Method/class/struct/interface/enum declarations ending with {
    # if/else/for/foreach/while/do/switch bodies
    # try/catch/finally blocks
    # using with body
    # namespace/class/struct/record bodies
    patterns = [
        r'^(public|private|protected|internal|static|async|virtual|override|abstract|sealed|partial|new|readonly|extern)\s+',
        r'^(class|struct|interface|enum|record|namespace)\s+',
        r'^(if|else|for|foreach|while|do|switch|try|catch|finally|using|lock)\b',
        r'^(get|set|init|add|remove)\s*(\{|=>)',
        r'^(case|default)\b',
    ]
    for p in patterns:
        if re.match(p, stripped):
            return True
    # Lambda with multiple statement body: line ends with '=>' or '{'  (but not single expression)
    return False


def is_sql_line(line):
    """Heuristic: line is part of a SQL string (inside verbatim @"..."/interpolated)."""
    stripped = line.strip()
    # Lines that look like SQL keywords (indented SQL inside verbatim strings)
    sql_keywords = ['select ', 'from ', 'where ', 'inner join', 'left join', 'right join',
                    'union all', 'union ', 'order by', 'group by', 'limit ', 'offset ',
                    'insert ', 'update ', 'delete ', 'on duplicate', 'values ', 'coalesce(',
                    'with recursive']
    for kw in sql_keywords:
        if stripped.lower().startswith(kw):
            return True
    return False


def should_skip_line(stripped):
    """Lines we never modify."""
    if stripped.startswith('///'):
        return True
    if stripped.startswith('#region') or stripped.startswith('#endregion'):
        return True
    return False


def collapse_file(content):
    """Main collapsing logic."""
    lines = content.split('\n')
    out = []
    i = 0
    changed = False

    while i < len(lines):
        line = lines[i]
        stripped = line.rstrip()
        inner = stripped.lstrip()

        # Never touch doc comments, regions
        if should_skip_line(inner):
            out.append(stripped)
            i += 1
            continue

        # Never touch SQL lines (they're part of verbatim strings)
        if is_sql_line(inner):
            out.append(stripped)
            i += 1
            continue

        # Accumulate lines to potentially join
        # Strategy: check if this line has unbalanced parens or ends with a joining token
        accumulated = stripped
        j = i + 1

        # Repeatedly try to join with next line if conditions are met
        max_lookahead = 30  # safety limit
        lookahead = 0

        while j < len(lines) and lookahead < max_lookahead:
            next_line = lines[j]
            next_inner = next_line.strip()

            # Never join across doc comments, SQL lines, regions
            if should_skip_line(next_inner) or is_sql_line(next_inner):
                break

            # Check: is there an unbalanced paren/bracket on accumulated?
            net_parens = count_open_parens(accumulated)
            net_braces = count_open_braces(accumulated)

            # Case 1: unbalanced open parens => we need to join
            if net_parens > 0:
                # But don't join if next line is an SQL string or block opener
                if is_string_continuation(next_inner):
                    break
                # Don't join if next is a lambda body with multiple statements
                # Join: collapse whitespace and append
                # Remove trailing comma alignment spaces
                next_content = next_inner
                # Strip trailing comma before } in initializers
                joined = accumulated.rstrip() + ' ' + next_content
                accumulated = joined
                j += 1
                lookahead += 1
                changed = True
                continue

            # Case 2: line ends with ',' and braces are 0 (last arg of a call that was
            # broken across lines — but parens are balanced here, meaning
            # the comma is trailing in an initializer)
            # Actually if net_parens == 0 and line ends with ',' then it might be
            # a parameter list item. Check brace context.
            # Actually for object initializers we need brace tracking too.

            # Case 3: next line starts with a chaining token
            if next_inner and next_inner[0] in ('.', '?') and net_parens == 0 and net_braces == 0:
                # Fluent chain or ternary continuation
                if is_string_continuation(next_inner):
                    break
                joined = accumulated.rstrip() + next_inner
                accumulated = joined
                j += 1
                lookahead += 1
                changed = True
                continue

            # Case 4: next line starts with ':' (ternary else branch)
            if next_inner.startswith(':') and not next_inner.startswith('::') and net_parens == 0:
                if is_string_continuation(next_inner):
                    break
                joined = accumulated.rstrip() + ' ' + next_inner
                accumulated = joined
                j += 1
                lookahead += 1
                changed = True
                continue

            # Case 5: current line ends with certain operators suggesting continuation
            acc_code = accumulated.rstrip()
            # Remove inline comment for check
            acc_no_comment = re.sub(r'\s*//.*$', '', acc_code).rstrip()

            if acc_no_comment.endswith(('&&', '||', '??', '+', '->', '=>')):
                # These suggest expression continuation
                if not is_string_continuation(next_inner):
                    joined = acc_code + ' ' + next_inner
                    accumulated = joined
                    j += 1
                    lookahead += 1
                    changed = True
                    continue

            # Case 6: object initializer — line ends with '{' and it's NOT a block opener
            # Check for: new Foo { or = new Foo() {
            if acc_no_comment.endswith('{'):
                # Check if this is an object initializer (not a method/control flow body)
                # Object initializer pattern: ends with '= new Type {' or 'return new Type {'
                # or 'new Type(' ... ') {' (constructor + initializer)
                is_obj_init = bool(re.search(r'\bnew\s+\w[\w<>\[\],\s]*\s*\(.*\)\s*\{$', acc_no_comment) or
                                   re.search(r'\bnew\s+\w[\w<>\[\],\s]*\s*\{$', acc_no_comment) or
                                   re.search(r'=\s*new\s+', acc_no_comment) or
                                   re.search(r'return\s+new\s+', acc_no_comment))
                is_control = bool(re.match(r'\s*(if|else|for|foreach|while|do|switch|try|catch|finally|using|lock|class|struct|interface|enum|record|namespace|get|set|init|add|remove)\b', acc_no_comment))
                is_method_decl = bool(re.match(r'\s*(public|private|protected|internal|static|async|virtual|override|abstract|sealed|partial|new|readonly|extern)\b', acc_no_comment) and
                                      not re.search(r'\bnew\s+\w', acc_no_comment))

                if is_obj_init and not is_control and not is_method_decl:
                    # Join initializer properties
                    if not is_string_continuation(next_inner):
                        joined = acc_code + ' ' + next_inner
                        accumulated = joined
                        j += 1
                        lookahead += 1
                        changed = True
                        continue

            # Case 7: accumulated ends with '{' inside object initializer context
            # We've started joining an init and next line has 'Prop = val,' or '};' or '}'
            if net_braces > 0 and not is_block_opener(accumulated.lstrip()):
                # We're inside an unbalanced brace context that started from an obj initializer
                if not is_string_continuation(next_inner):
                    joined = acc_code + ' ' + next_inner
                    accumulated = joined
                    j += 1
                    lookahead += 1
                    changed = True
                    continue

            # No more joining needed
            break

        # Clean up trailing comma before closing brace in object initializers
        # Pattern: ", }" -> " }"
        accumulated = re.sub(r',\s*\}', ' }', accumulated)
        # Also handle ", };" -> " };"
        accumulated = re.sub(r',\s*\};', ' };', accumulated)

        # Normalize multiple spaces (but not inside strings - be careful)
        # Actually just normalize spaces between tokens outside of strings
        # This is risky so let's skip aggressive normalization

        out.append(accumulated)
        if j > i + 1 or accumulated != stripped:
            changed = True
        i = j if j > i + 1 else i + 1

    result = '\n'.join(out)
    return result, changed


def process_files():
    files = get_cs_files(SOURCE_ROOT)
    changed_files = []

    for fpath in sorted(files):
        with open(fpath, 'r', encoding='utf-8-sig') as f:
            original = f.read()

        new_content, changed = collapse_file(original)

        if changed and new_content != original:
            with open(fpath, 'w', encoding='utf-8') as f:
                f.write(new_content)
            changed_files.append(fpath)
            print(f"  CHANGED: {os.path.relpath(fpath, SOURCE_ROOT)}")
        else:
            print(f"  unchanged: {os.path.relpath(fpath, SOURCE_ROOT)}")

    print(f"\nTotal files changed: {len(changed_files)}")
    return changed_files


if __name__ == '__main__':
    process_files()
