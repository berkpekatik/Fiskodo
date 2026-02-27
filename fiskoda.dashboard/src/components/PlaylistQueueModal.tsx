import { useEffect, useState } from 'react'
import { getGuildQueue } from '@/lib/api'
import { formatDurationMs } from '@/lib/format'
import type { GuildQueueDto } from '@/types/api'

type PlaylistQueueModalProps = {
  open: boolean
  onClose: () => void
  guildId: string
  guildName: string
}

export default function PlaylistQueueModal({ open, onClose, guildId, guildName }: PlaylistQueueModalProps) {
  const [data, setData] = useState<GuildQueueDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open || !guildId) return
    setData(null)
    setError(null)
    setLoading(true)
    getGuildQueue(guildId)
      .then(setData)
      .catch(() => setError('Failed to load queue'))
      .finally(() => setLoading(false))
  }, [open, guildId])

  if (!open) return null

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
      onClick={(e) => e.target === e.currentTarget && onClose()}
      role="dialog"
      aria-modal="true"
      aria-labelledby="queue-modal-title"
    >
      <div
        className="w-full max-w-md max-h-[85vh] flex flex-col rounded-xl bg-[#36393f] shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-[#40444b] px-5 py-4">
          <h2 id="queue-modal-title" className="text-lg font-semibold text-white">
            {guildName} — Kalan şarkılar
          </h2>
          <button
            type="button"
            onClick={onClose}
            className="rounded p-1.5 text-[#b9bbbe] hover:bg-[#40444b] hover:text-white transition-colors"
            aria-label="Close"
          >
            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        <div className="flex-1 overflow-y-auto px-5 py-4 scrollbar-dark min-h-0">
          {loading && (
            <div className="space-y-3">
              <div className="h-5 w-3/4 bg-[#40444b] rounded animate-pulse" />
              <div className="h-10 bg-[#40444b] rounded animate-pulse" />
              <div className="h-10 bg-[#40444b] rounded animate-pulse" />
              <div className="h-10 bg-[#40444b] rounded animate-pulse" />
            </div>
          )}
          {error && <p className="text-red-400 text-sm">{error}</p>}
          {!loading && !error && data && (
            <>
              <p className="text-sm text-[#b9bbbe] mb-3">
                Şu an çalan: <span className="font-medium text-white">{data.nowPlayingTitle ?? 'Idle'}</span>
              </p>
              {data.remaining.length === 0 ? (
                <p className="text-sm text-[#72767d]">Kuyrukta şarkı yok.</p>
              ) : (
                <ul className="space-y-2">
                  {data.remaining.map((item, index) => (
                    <li
                      key={`${index}-${item.title}`}
                      className="rounded-lg bg-[#2c2f33]/80 px-3 py-2.5 flex items-center justify-between gap-2"
                    >
                      <span className="text-sm font-medium text-white truncate flex-1 min-w-0">
                        {index + 1}. {item.title}
                      </span>
                      <div className="flex items-center gap-2 flex-shrink-0 text-xs text-[#b9bbbe]">
                        {item.author && <span className="hidden sm:inline truncate max-w-[100px]">{item.author}</span>}
                        {item.durationMs != null && (
                          <span className="text-[#72767d]">{formatDurationMs(item.durationMs)}</span>
                        )}
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </>
          )}
        </div>

        <div className="border-t border-[#40444b] px-5 py-3">
          <button
            type="button"
            onClick={onClose}
            className="w-full py-2 rounded-lg bg-[#5865f2] hover:bg-[#4752c4] text-white font-medium transition-colors"
          >
            Kapat
          </button>
        </div>
      </div>
    </div>
  )
}
