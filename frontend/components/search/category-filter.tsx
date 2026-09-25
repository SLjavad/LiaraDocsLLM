"use client";

import { useEffect, useState } from "react";
import { useSession } from "@/contexts/session-provider";
import { getCategories } from "@/lib/api";
import { t } from "@/lib/i18n";
import type { Category } from "@/lib/types";

export function CategoryFilter({
  value,
  onChange,
}: {
  value: string | null;
  onChange: (value: string | null) => void;
}) {
  const { locale } = useSession();
  const [categories, setCategories] = useState<Category[]>([]);

  useEffect(() => {
    getCategories()
      .then(({ categories }) => setCategories(categories))
      .catch(() => setCategories([]));
  }, []);

  return (
    <select
      dir="auto"
      value={value ?? ""}
      onChange={(e) => onChange(e.target.value || null)}
      className="border-input bg-background h-9 rounded-md border px-3 text-sm"
    >
      <option value="">{t(locale).search.categoryAll}</option>
      {categories.map((c) => (
        <option key={c.id} value={c.id}>
          {locale === "fa" ? c.labelFa : c.labelEn}
        </option>
      ))}
    </select>
  );
}
