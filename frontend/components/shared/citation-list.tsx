import type { Citation } from "@/lib/types";
import { useSession } from "@/contexts/session-provider";
import { t } from "@/lib/i18n";

export function CitationList({ citations }: { citations: Citation[] }) {
  const { locale } = useSession();
  if (citations.length === 0) return null;

  return (
    <div className="mt-3 border-t pt-2">
      <p className="text-muted-foreground mb-1 text-xs font-medium">{t(locale).common.sources}</p>
      <ul className="flex flex-col gap-1">
        {citations.map((c, i) => (
          <li key={`${c.url}-${c.anchor ?? ""}-${i}`}>
            <a
              href={c.anchor ? `${c.url}${c.anchor}` : c.url}
              target="_blank"
              rel="noopener noreferrer"
              dir="auto"
              className="text-primary text-xs underline underline-offset-2 hover:opacity-80"
            >
              {c.title}
            </a>
          </li>
        ))}
      </ul>
    </div>
  );
}
