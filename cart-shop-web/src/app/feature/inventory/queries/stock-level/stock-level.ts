import { Component, effect, inject, input, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';

// Mirrors CartShop.Core/Feature/Inventory/Queries/StockLevel/Handler.cs
interface StockLevelResponse {
  sku: string;
  stock: number;
  reserved: number;
  available: number;
}

@Component({
  selector: 'cs-stock-level',
  template: `
    <span class="stock-pill" [class.empty]="(view()?.available ?? 1) === 0">
      @if (view(); as v) {
        <code>{{ v.sku }}</code>
        <span class="num">{{ v.available }}</span>
        <small>/ {{ v.stock }}</small>
        <small class="muted">(reserved {{ v.reserved }})</small>
      } @else {
        <code>{{ sku() }}</code> <small class="muted">no stock</small>
      }
    </span>
  `,
  styles: [`
    .stock-pill {
      display: inline-flex; align-items: baseline; gap: 6px;
      padding: 4px 10px; border-radius: 12px;
      background: #e6f4ea; color: #2f855a; font-size: 13px;
    }
    .stock-pill.empty { background: #fdecea; color: #c0392b; }
    .num { font-weight: 600; }
    .muted { opacity: 0.7; }
  `],
})
export class StockLevel {
  private http = inject(HttpClient);

  sku = input.required<string>();
  refreshKey = input(0);

  view = signal<StockLevelResponse | null>(null);

  constructor() {
    effect(() => {
      this.refreshKey(); // dep
      const s = this.sku();
      if (s) this.load(s);
    });
  }

  private async load(sku: string) {
    try {
      const v = await this.http.get<StockLevelResponse>(`/api/inventory/${encodeURIComponent(sku)}`).toPromise();
      this.view.set(v ?? null);
    } catch {
      this.view.set(null);
    }
  }
}
