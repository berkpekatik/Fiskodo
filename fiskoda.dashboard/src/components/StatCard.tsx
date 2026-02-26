import { type ReactNode } from 'react'

type StatCardProps = {
  label: string
  value: string | number
  icon: ReactNode
  iconColor: string
}

export default function StatCard({ label, value, icon, iconColor }: StatCardProps) {
  return (
    <div className="rounded-xl bg-[#36393f] p-5">
      <div className="flex items-center gap-2 mb-2">
        <span className={iconColor}>{icon}</span>
        <span className="text-xs font-medium text-[#b9bbbe] uppercase tracking-wide">
          {label}
        </span>
      </div>
      <p className="text-2xl font-bold text-white">{value}</p>
    </div>
  )
}
