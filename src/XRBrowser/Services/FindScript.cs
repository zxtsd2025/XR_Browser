namespace XRBrowser.Services;

public static class FindScript
{
    public const string JavaScript = """
        (() => {
          if (window.__xrFind) return;
          const state = { query: '', ranges: [], index: -1 };
          let styleInstalled = false;

          function installStyle() {
            if (styleInstalled || !document.documentElement) return;
            styleInstalled = true;
            const style = document.createElement('style');
            style.id = '__xr_find_style';
            style.textContent = `
              ::highlight(xr-find) { background-color: #FDD663; color: #111; }
              ::highlight(xr-find-current) { background-color: #F9AB00; color: #111; }
            `;
            document.documentElement.appendChild(style);
          }

          function clearHighlights() {
            try {
              if (window.CSS && CSS.highlights) {
                CSS.highlights.delete('xr-find');
                CSS.highlights.delete('xr-find-current');
              }
            } catch (_) { }
          }

          function collect(query) {
            const ranges = [];
            if (!query) return ranges;
            const root = document.body || document.documentElement;
            if (!root) return ranges;
            const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
              acceptNode(node) {
                if (!node.nodeValue || !node.nodeValue.length) return NodeFilter.FILTER_REJECT;
                const parent = node.parentElement;
                if (!parent) return NodeFilter.FILTER_REJECT;
                const tag = parent.tagName;
                if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'NOSCRIPT' || tag === 'TEXTAREA') return NodeFilter.FILTER_REJECT;
                if (parent.isContentEditable) return NodeFilter.FILTER_REJECT;
                return NodeFilter.FILTER_ACCEPT;
              }
            });
            const needle = query.toLocaleLowerCase();
            while (walker.nextNode()) {
              const text = walker.currentNode.nodeValue;
              const haystack = text.toLocaleLowerCase();
              let start = 0;
              while (start <= haystack.length) {
                const at = haystack.indexOf(needle, start);
                if (at < 0) break;
                const range = document.createRange();
                range.setStart(walker.currentNode, at);
                range.setEnd(walker.currentNode, at + query.length);
                ranges.push(range);
                start = at + Math.max(1, needle.length);
              }
            }
            return ranges;
          }

          function paint() {
            installStyle();
            clearHighlights();
            if (!state.ranges.length) return;
            try {
              if (window.CSS && CSS.highlights && window.Highlight) {
                CSS.highlights.set('xr-find', new Highlight(...state.ranges));
                const current = state.ranges[state.index];
                if (current) CSS.highlights.set('xr-find-current', new Highlight(current));
              }
              const range = state.ranges[state.index];
              if (range) {
                const rect = range.getBoundingClientRect();
                if (rect.top < 0 || rect.bottom > window.innerHeight) {
                  const el = range.startContainer.parentElement;
                  if (el) el.scrollIntoView({ block: 'center', inline: 'nearest' });
                }
                const sel = window.getSelection && window.getSelection();
                if (sel) {
                  sel.removeAllRanges();
                  sel.addRange(range);
                }
              }
            } catch (_) { }
          }

          function label() {
            if (!state.ranges.length) return '0/0';
            return `${state.index + 1}/${state.ranges.length}`;
          }

          window.__xrFind = {
            search(query) {
              state.query = query || '';
              state.ranges = collect(state.query);
              state.index = state.ranges.length ? 0 : -1;
              if (!state.query) clearHighlights();
              else paint();
              return label();
            },
            next(direction) {
              if (!state.ranges.length) return '0/0';
              state.index = (state.index + (direction || 1) + state.ranges.length) % state.ranges.length;
              paint();
              return label();
            },
            clear() {
              state.query = '';
              state.ranges = [];
              state.index = -1;
              clearHighlights();
              try { window.getSelection()?.removeAllRanges(); } catch (_) { }
              return '0/0';
            }
          };
        })();
        """;
}
