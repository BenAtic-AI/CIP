type SectionCardProps = {
  title: string
  description: string
  bullets: string[]
}

export function SectionCard({ title, description, bullets }: SectionCardProps) {
  return (
    <section className="rounded-2xl border border-slate-800 bg-slate-900/70 p-6 shadow-lg shadow-slate-950/20">
      <h2 className="text-lg font-semibold text-white">{title}</h2>
      <p className="mt-2 text-sm text-slate-300">{description}</p>
      <ul className="mt-4 space-y-2 text-sm text-slate-400">
        {bullets.map((bullet) => (
          <li key={bullet} className="flex gap-2">
            <span className="text-cyan-400">•</span>
            <span>{bullet}</span>
          </li>
        ))}
      </ul>
    </section>
  )
}
