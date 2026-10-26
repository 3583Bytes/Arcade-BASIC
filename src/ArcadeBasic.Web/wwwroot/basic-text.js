// Small facts about a line of BASIC that the editor's tools need.

/** Where the line's comment starts (a REM statement, or "!" outside a string), or its length. */
export function commentStart(text) {
  const rem = /^\s*(\d+\s+)?REM\b/i.exec(text);
  if (rem) return rem[0].length - 3;
  let inString = false;
  for (let i = 0; i < text.length; i++) {
    if (text[i] === '"') inString = !inString;
    else if (text[i] === '!' && !inString) return i;
  }
  return text.length;
}
