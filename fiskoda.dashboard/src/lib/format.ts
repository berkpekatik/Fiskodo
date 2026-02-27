/** Parse .NET TimeSpan string (e.g. "1.23:45:00" or "00:00:00.042") and format for display */
export function formatUptime(value: string): string {
  const parsed = parseTimeSpan(value)
  if (parsed == null) return value
  const { days, hours, minutes, seconds } = parsed
  const parts: string[] = []
  if (days > 0) parts.push(String(days).padStart(2, '0'))
  parts.push(String(hours).padStart(2, '0'))
  parts.push(String(minutes).padStart(2, '0'))
  parts.push(String(seconds).padStart(2, '0'))
  return parts.join(':')
}

/** Parse .NET TimeSpan and return total milliseconds (for latency display) */
export function formatLatency(value: string): string {
  const ms = parseTimeSpanToMs(value)
  if (ms == null) return value
  return `${Math.round(ms)}ms`
}

/** Format duration in milliseconds as m:ss */
export function formatDurationMs(ms: number): string {
  const totalSeconds = Math.floor(ms / 1000)
  const m = Math.floor(totalSeconds / 60)
  const s = totalSeconds % 60
  return `${m}:${String(s).padStart(2, '0')}`
}

function parseTimeSpan(s: string): { days: number; hours: number; minutes: number; seconds: number } | null {
  if (!s || typeof s !== 'string') return null
  const d = parseTimeSpanToMs(s)
  if (d == null) return null
  const totalSeconds = Math.floor(d / 1000)
  const days = Math.floor(totalSeconds / 86400)
  const rest = totalSeconds % 86400
  const hours = Math.floor(rest / 3600)
  const rest2 = rest % 3600
  const minutes = Math.floor(rest2 / 60)
  const seconds = rest2 % 60
  return { days, hours, minutes, seconds }
}

function parseTimeSpanToMs(s: string): number | null {
  if (!s || typeof s !== 'string') return null
  const trimmed = s.trim()
  if (!trimmed) return null

  // .NET formats: "d.HH:mm:ss" (e.g. "1.23:45:00") or "HH:mm:ss.fffffff" (e.g. "00:01:34.6441195", "00:00:00.1625836")
  const dotIndex = trimmed.indexOf('.')
  let totalSeconds: number

  if (dotIndex === -1) {
    // No dot: "HH:mm:ss" or "H:mm:ss"
    const parts = trimmed.split(':').map((x) => parseInt(x, 10) || 0)
    const [h = 0, m = 0, sec = 0] = parts
    totalSeconds = h * 3600 + m * 60 + sec
  } else {
    const beforeDot = trimmed.slice(0, dotIndex)
    const afterDot = trimmed.slice(dotIndex + 1)

    if (beforeDot.includes(':')) {
      // "HH:mm:ss.fffffff" — time with fractional seconds
      const [h = 0, m = 0, sec = 0] = beforeDot.split(':').map((x) => parseInt(x, 10) || 0)
      const fracSec = afterDot ? parseFloat('0.' + afterDot) : 0
      totalSeconds = h * 3600 + m * 60 + sec + fracSec
    } else {
      // "d.HH:mm:ss" — days then time
      const days = parseInt(beforeDot, 10) || 0
      const [h = 0, m = 0, sec = 0] = afterDot.split(':').map((x) => parseInt(x, 10) || 0)
      totalSeconds = days * 86400 + h * 3600 + m * 60 + sec
    }
  }

  return totalSeconds * 1000
}
