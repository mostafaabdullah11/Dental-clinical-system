import { useAuth } from '../auth/AuthContext';
import { DoctorHome } from './home/DoctorHome';
import { ManagerHome } from './home/ManagerHome';
import { SalesHome } from './home/SalesHome';

/**
 * The role's home. The Manager and Sales dashboards are the frozen FR-REPORT-001 / FR-REPORT-009 views (their permissions decide);
 * the Doctor has a start page only (there is no Doctor dashboard permission or route in V1).
 */
export function HomePage() {
  const { me, can } = useAuth();
  if (!me) {
    return null;
  }

  if (can('Reports.Dashboard.Manager')) {
    return <ManagerHome />;
  }
  if (can('Reports.Dashboard.Sales')) {
    return <SalesHome />;
  }
  return me.role === 'DOCTOR' ? <DoctorHome /> : null;
}
