import sys

def count_braces(path):
    with open(path, encoding='utf-8') as f:
        lines = f.readlines()
    depth = 0
    in_string = False
    in_verbatim = False
    in_char = False
    for i, line in enumerate(lines, 1):
        j = 0
        raw = line.rstrip('\n')
        while j < len(raw):
            c = raw[j]
            if in_verbatim:
                if c == '"':
                    if j+1 < len(raw) and raw[j+1] == '"':
                        j += 2
                        continue
                    in_verbatim = False
            elif in_string:
                if c == '\\':
                    j += 2
                    continue
                if c == '"':
                    in_string = False
            elif in_char:
                if c == '\\':
                    j += 2
                    continue
                if c == "'":
                    in_char = False
            else:
                if c == '/' and j+1 < len(raw) and raw[j+1] == '/':
                    break  # line comment eats rest
                if c == '@' and j+1 < len(raw) and raw[j+1] == '"':
                    in_verbatim = True
                    j += 2
                    continue
                if c == '"':
                    in_string = True
                elif c == "'":
                    in_char = True
                elif c == '{':
                    depth += 1
                elif c == '}':
                    depth -= 1
            j += 1
        if depth < 0:
            print(f"Line {i}: NEGATIVE depth {depth}! -> {raw[:100]}")
            break
        if i <= 40 or i >= 150:
            print(f"Line {i}: depth={depth}")
    print(f"Final depth: {depth}")

count_braces(sys.argv[1])
