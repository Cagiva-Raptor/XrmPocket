import { forwardRef, useEffect, useImperativeHandle, useRef } from 'react'
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands'
import { sql } from '@codemirror/lang-sql'
import { Compartment, EditorState } from '@codemirror/state'
import { EditorView, highlightActiveLineGutter, keymap, lineNumbers } from '@codemirror/view'
import { oneDark } from '@codemirror/theme-one-dark'
import { vim } from '@replit/codemirror-vim'

export interface SqlEditorHandle {
  getSelectedText: () => string
  focus: () => void
}

interface Props {
  value: string
  onChange: (value: string) => void
  onRun: () => void
  vimMode: boolean
}

export const SqlEditor = forwardRef<SqlEditorHandle, Props>(function SqlEditor({ value, onChange, onRun, vimMode }, ref) {
  const host = useRef<HTMLDivElement>(null)
  const view = useRef<EditorView | null>(null)
  const vimCompartment = useRef(new Compartment())
  const onChangeRef = useRef(onChange)
  const onRunRef = useRef(onRun)

  onChangeRef.current = onChange
  onRunRef.current = onRun

  useEffect(() => {
    if (!host.current) return

    const editor = new EditorView({
      parent: host.current,
      state: EditorState.create({
        doc: value,
        extensions: [
          vimCompartment.current.of(vimMode ? vim() : []),
          lineNumbers(),
          highlightActiveLineGutter(),
          history(),
          keymap.of([
            {
              key: 'Mod-Enter',
              run: () => {
                onRunRef.current()
                return true
              },
            },
            indentWithTab,
            ...defaultKeymap,
            ...historyKeymap,
          ]),
          sql(),
          oneDark,
          EditorView.lineWrapping,
          EditorView.theme({
            '&': { fontSize: '14px', borderRadius: '12px', height: '100%' },
            '.cm-scroller': { fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace', lineHeight: '1.55' },
          }),
          EditorView.updateListener.of((update) => {
            if (update.docChanged) onChangeRef.current(update.state.doc.toString())
          }),
        ],
      }),
    })

    view.current = editor

    return () => {
      editor.destroy()
      view.current = null
    }
    // De editor wordt één keer aangemaakt; waarden worden hieronder bijgehouden.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    const editor = view.current
    if (!editor || editor.state.doc.toString() === value) return

    editor.dispatch({ changes: { from: 0, to: editor.state.doc.length, insert: value } })
  }, [value])

  useEffect(() => {
    view.current?.dispatch({ effects: vimCompartment.current.reconfigure(vimMode ? vim() : []) })
  }, [vimMode])

  useImperativeHandle(
    ref,
    () => ({
      getSelectedText: () => {
        const editor = view.current
        if (!editor) return ''
        const range = editor.state.selection.main
        return editor.state.sliceDoc(range.from, range.to)
      },
      focus: () => view.current?.focus(),
    }),
    [],
  )

  return <div ref={host} className="sql-codemirror" />
})
