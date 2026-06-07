import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TaskDashboardComponent } from './task-dashboard.component';
import { ApiService } from '../../services/api.service';
import { of } from 'rxjs';

describe('TaskDashboardComponent', () => {
  let apiServiceSpy: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    apiServiceSpy = jasmine.createSpyObj('ApiService', ['getTasks', 'getRepositories']);
    apiServiceSpy.getTasks.and.returnValue(of([]));
    apiServiceSpy.getRepositories.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [TaskDashboardComponent],
      providers: [
        { provide: ApiService, useValue: apiServiceSpy },
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(TaskDashboardComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should load tasks on init', () => {
    const fixture = TestBed.createComponent(TaskDashboardComponent);
    fixture.detectChanges();
    expect(apiServiceSpy.getTasks).toHaveBeenCalled();
  });

  it('should default to active tab', () => {
    const fixture = TestBed.createComponent(TaskDashboardComponent);
    expect(fixture.componentInstance.tab).toBe('active');
  });

  it('should filter tasks by status', () => {
    apiServiceSpy.getTasks.and.returnValue(of([
      { id: '1', operation: 'Clone', status: 'Running', progress: 50, createdAt: '' },
      { id: '2', operation: 'Scan', status: 'Pending', progress: 0, createdAt: '' },
      { id: '3', operation: 'Index', status: 'Completed', progress: 100, createdAt: '' },
      { id: '4', operation: 'Enrich', status: 'Failed', progress: 0, errorMessage: 'err', createdAt: '' }
    ] as any));
    const fixture = TestBed.createComponent(TaskDashboardComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.activeTasks.length).toBe(1);
    expect(fixture.componentInstance.pendingTasks.length).toBe(1);
    expect(fixture.componentInstance.completedTasks.length).toBe(1);
    expect(fixture.componentInstance.failedTasks.length).toBe(1);
  });

  it('should return correct status class', () => {
    const fixture = TestBed.createComponent(TaskDashboardComponent);
    expect(fixture.componentInstance.statusClass('Running')).toBe('badge-info');
    expect(fixture.componentInstance.statusClass('Completed')).toBe('badge-success');
    expect(fixture.componentInstance.statusClass('Failed')).toBe('badge-danger');
    expect(fixture.componentInstance.statusClass('Pending')).toBe('badge-muted');
  });

  it('should clean up polling on destroy', () => {
    const fixture = TestBed.createComponent(TaskDashboardComponent);
    fixture.detectChanges();
    spyOn(window, 'clearInterval');
    fixture.destroy();
    expect(window.clearInterval).toHaveBeenCalled();
  });
});
