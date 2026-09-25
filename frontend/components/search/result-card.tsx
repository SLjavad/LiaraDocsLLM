import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import type { SearchResult } from "@/lib/types";

export function ResultCard({ result }: { result: SearchResult }) {
  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm font-medium">
          <a
            href={result.anchor ? `${result.url}${result.anchor}` : result.url}
            target="_blank"
            rel="noopener noreferrer"
            dir="auto"
            className="text-primary underline underline-offset-2 hover:opacity-80"
          >
            {result.title}
          </a>
        </CardTitle>
      </CardHeader>
      <CardContent className="flex flex-col gap-2">
        <p dir="auto" className="text-muted-foreground text-sm">
          {result.snippet}
        </p>
        <div className="flex items-center gap-2">
          <Badge variant="secondary">{result.category}</Badge>
          <span className="text-muted-foreground text-xs">{result.score.toFixed(2)}</span>
        </div>
      </CardContent>
    </Card>
  );
}
