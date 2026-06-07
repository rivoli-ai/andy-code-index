import { TestBed, ComponentFixture } from '@angular/core/testing';
import { of, BehaviorSubject } from 'rxjs';
import { provideRouter } from '@angular/router';
import { DashboardComponent } from './dashboard.component';
import { ApiService } from '../../services/api.service';
import { PinService } from '../../services/pin.service';
import { HealthService } from '../../services/health.service';

describe('DashboardComponent', () => {
  let apiSpy: jasmine.SpyObj<ApiService>;
  let pinSpy: jasmine.SpyObj<PinService>;

  function setup(pinnedIds: string[]): ComponentFixture<DashboardComponent> {
    apiSpy = jasmine.createSpyObj('ApiService', ['getRepository', 'getBulkSparklines', 'syncRepository']);
    apiSpy.getBulkSparklines.and.returnValue(of({}));
    apiSpy.syncRepository.and.returnValue(of({} as any));
    pinSpy = jasmine.createSpyObj('PinService', ['getPinnedIds']);
    pinSpy.getPinnedIds.and.returnValue(pinnedIds);

    TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        { provide: ApiService, useValue: apiSpy },
        { provide: PinService, useValue: pinSpy },
        { provide: HealthService, useValue: { isConnected$: new BehaviorSubject(true) } },
        provideRouter([]),
      ],
    });
    return TestBed.createComponent(DashboardComponent);
  }

  it('should not fetch repositories when nothing is pinned', () => {
    const fixture = setup([]);
    fixture.detectChanges();
    expect(fixture.componentInstance.loading).toBeFalse();
    expect(fixture.componentInstance.pinnedRepos.length).toBe(0);
    expect(apiSpy.getRepository).not.toHaveBeenCalled();
  });

  it('should load pinned repositories and their sparklines', () => {
    const fixture = setup(['r1', 'r2']);
    apiSpy.getRepository.and.callFake((id: string) => of({ id } as any));
    fixture.detectChanges();

    expect(apiSpy.getRepository).toHaveBeenCalledTimes(2);
    expect(fixture.componentInstance.pinnedRepos.map(r => r.id)).toEqual(['r1', 'r2']);
    expect(apiSpy.getBulkSparklines).toHaveBeenCalled();
    expect(fixture.componentInstance.loading).toBeFalse();
  });

  it('should sync a repository through the api', () => {
    const fixture = setup([]);
    fixture.detectChanges();
    fixture.componentInstance.sync({ id: 'r1' } as any);
    expect(apiSpy.syncRepository).toHaveBeenCalledWith('r1');
  });
});
