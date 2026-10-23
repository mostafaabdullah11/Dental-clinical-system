import { Navigate, Route, Routes } from 'react-router-dom';
import { useAuth } from './auth/AuthContext';
import { AppShell } from './layout/AppShell';
import { AssistantsPage } from './pages/AssistantsPage';
import { ChangePasswordPage } from './pages/ChangePasswordPage';
import { DoctorsPage } from './pages/DoctorsPage';
import { FollowUpPage } from './pages/FollowUpPage';
import { FollowUpsPage } from './pages/FollowUpsPage';
import { HomePage } from './pages/HomePage';
import { LeadPage } from './pages/LeadPage';
import { LeadsPage } from './pages/LeadsPage';
import { LoginPage } from './pages/LoginPage';
import { PatientPage } from './pages/PatientPage';
import { PatientsSearchPage } from './pages/PatientsSearchPage';
import { RegisterPatientPage } from './pages/RegisterPatientPage';
import { SalesPage } from './pages/SalesPage';
import { SettingsPage } from './pages/SettingsPage';
import { UsersPage } from './pages/UsersPage';

export function App() {
  const { me, loading } = useAuth();

  if (loading) {
    return <p className="centered">جارٍ التحميل…</p>;
  }

  if (!me) {
    return (
      <Routes>
        <Route path="*" element={<LoginPage />} />
      </Routes>
    );
  }

  // Route guards are usability only; the backend enforces every permission.
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<HomePage />} />
        <Route path="patients" element={<PatientsSearchPage />} />
        <Route path="patients/new" element={<RegisterPatientPage />} />
        <Route path="patients/:id" element={<PatientPage />} />
        <Route path="leads" element={<LeadsPage />} />
        <Route path="leads/:id" element={<LeadPage />} />
        <Route path="follow-ups" element={<FollowUpsPage />} />
        <Route path="follow-ups/:id" element={<FollowUpPage />} />
        <Route path="users" element={<UsersPage />} />
        <Route path="doctors" element={<DoctorsPage />} />
        <Route path="sales" element={<SalesPage />} />
        <Route path="assistants" element={<AssistantsPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="change-password" element={<ChangePasswordPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}
