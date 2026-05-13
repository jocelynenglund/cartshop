import { Component, effect, inject, input, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';

// Mirrors CartShop.Core/Feature/Cart/Queries/ListSubmittedCarts/Handler.cs
interface SubmittedCartSummary {
  id: string;
  customerName: string;
  total: number;
  submittedAt?: string;
  lineCount: number;
}

@Component({
  selector: 'cs-list-submitted-carts',
  imports: [CommonModule],
  template: `
    <section class="card">
      <h2>Submitted carts</h2>
      @if (carts().length === 0) {
        <p class="empty">No submitted carts yet.</p>
      } @else {
        <table>
          <thead>
            <tr>
              <th>Customer</th>
              <th class="num">Items</th>
              <th class="num">Total</th>
              <th>Submitted</th>
            </tr>
          </thead>
          <tbody>
            @for (s of carts(); track s.id) {
              <tr>
                <td>{{ s.customerName }}</td>
                <td class="num">{{ s.lineCount }}</td>
                <td class="num">{{ s.total | number:'1.2-2' }}</td>
                <td>{{ s.submittedAt | date:'short' }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>
  `,
})
export class ListSubmittedCarts {
  private http = inject(HttpClient);

  refreshKey = input(0);

  carts = signal<SubmittedCartSummary[]>([]);

  constructor() {
    // Re-fetch when refreshKey changes (parent bumps it after submit).
    effect(() => {
      this.refreshKey();
      this.load();
    });
  }

  private async load() {
    try {
      const list = await this.http.get<SubmittedCartSummary[]>('/api/carts/submitted').toPromise();
      this.carts.set(list ?? []);
    } catch { /* ignore */ }
  }
}
